// Source-level invariant for the copy/paste module: a screen that IMPLEMENTS ICopyPasteSurface must
// declare CopyPasteBinding.Surface in its markup, and a markup declaration must have a code-behind
// that implements it. Driving the two screens that exist today proves today's behaviour and says
// nothing about the third one added next year — this constrains that one.
//
// Also sweeps for CJK outside the language files, per the skill's standing check.
const fs = require('node:fs');
const path = require('node:path');

const root = process.argv[2];
const skip = /\\(bin|obj|publish|\.git|AgentSkills)\\/i;

function walk(dir, out = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (skip.test(full + '\\')) continue;
    if (entry.isDirectory()) walk(full, out);
    else out.push(full);
  }
  return out;
}

const files = walk(root);
let problems = 0;
const fail = (msg) => { console.log('FAIL  ' + msg); problems++; };

// ── copy/paste surfaces ────────────────────────────────────────────────────────────────────────
const implementers = new Set();
for (const file of files.filter(f => f.endsWith('.cs'))) {
  const text = fs.readFileSync(file, 'utf8');
  // The interface declaration on a type, not a mention of it in a comment or a using.
  if (/\bclass\s+\w+\s*:[^{\r\n]*\bICopyPasteSurface\b/.test(text))
    implementers.add(path.basename(file).replace(/\.xaml\.cs$|\.cs$/, ''));
}

const declarers = new Set();
for (const file of files.filter(f => f.endsWith('.xaml'))) {
  const text = fs.readFileSync(file, 'utf8');
  if (/CopyPasteBinding\.Surface\s*=/.test(text))
    declarers.add(path.basename(file).replace(/\.xaml$/, ''));
}

console.log('implements ICopyPasteSurface :', [...implementers].join(', ') || '(none)');
console.log('declares CopyPasteBinding    :', [...declarers].join(', ') || '(none)');

for (const name of implementers)
  if (!declarers.has(name)) fail(`${name} implements ICopyPasteSurface but its XAML binds nothing to it`);

for (const name of declarers)
  if (!implementers.has(name)) fail(`${name}.xaml binds CopyPasteBinding.Surface but the code-behind does not implement it`);

if (implementers.size < 2)
  fail(`expected at least the orders list and Store Management, found ${implementers.size}`);

// The shortcut handling must not be re-typed per window: nothing outside the module may switch on
// Key.C / Key.V.
//
// Tests/ is exempt from THIS rule and only this one. A harness names those keys in order to ASSERT
// that the bindings exist, which is the opposite of the thing being prevented — the rule is about
// application code growing its own copy of the shortcut. The CJK sweep below still covers Tests/,
// because test code is source and the English-only rule has no exemptions.
const isApplicationCode = (f) => !/\\Tests\\/i.test(f);

for (const file of files.filter(f => f.endsWith('.cs') && isApplicationCode(f)
                                     && !f.endsWith('CopyPasteBinding.cs'))) {
  const text = fs.readFileSync(file, 'utf8');
  if (/Key\.[CV]\b/.test(text))
    fail(`${path.relative(root, file)} handles Key.C/Key.V itself — use CopyPasteBinding`);
}

// ── CJK outside the language files ─────────────────────────────────────────────────────────────
const cjk = /[぀-ヿ㐀-䶿一-鿿！-｠、。《-】]/;
const checked = /\.(cs|xaml|json|csproj|ps1)$/i;

for (const file of files.filter(f => checked.test(f) && !f.endsWith('.lang.xml'))) {
  const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/);
  lines.forEach((line, i) => {
    if (cjk.test(line)) fail(`CJK in ${path.relative(root, file)}:${i + 1}  ${line.trim().slice(0, 90)}`);
  });
}

console.log(problems === 0 ? 'OK — surfaces paired, shortcuts centralised, no CJK outside the language files'
                           : `FAILED with ${problems} problem(s)`);
process.exit(problems === 0 ? 0 : 1);

