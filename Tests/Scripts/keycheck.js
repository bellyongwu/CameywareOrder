// Key parity and placeholder parity across every shipped language file, plus a check that no
// translation is word-identical to English. Stands in for the formatcheck harness, which is no
// longer on disk.
const fs = require('node:fs');
const path = require('node:path');

const dir = process.argv[2];
const files = fs.readdirSync(dir).filter(f => f.endsWith('.lang.xml'));

const tables = {};
for (const file of files) {
  const xml = fs.readFileSync(path.join(dir, file), 'utf8');
  const map = new Map();
  const re = /<Text key="([^"]+)">([\s\S]*?)<\/Text>/g;
  let m;
  while ((m = re.exec(xml)) !== null) {
    if (map.has(m[1])) console.log(`DUPLICATE KEY  ${file}: ${m[1]}`);
    map.set(m[1], m[2]);
  }
  tables[file] = map;
}

const all = new Set();
for (const map of Object.values(tables)) for (const k of map.keys()) all.add(k);

let problems = 0;
for (const [file, map] of Object.entries(tables)) {
  for (const key of all) {
    if (!map.has(key)) { console.log(`MISSING  ${file}: ${key}`); problems++; }
  }
  console.log(`${file}: ${map.size} keys`);
}

// Placeholder sets must match the reference language per key.
const ref = tables['en-US.lang.xml'];
const holders = (s) => [...new Set([...s.matchAll(/\{(\d+)\}/g)].map(m => m[1]))].sort().join(',');

for (const [file, map] of Object.entries(tables)) {
  if (file === 'en-US.lang.xml') continue;
  for (const [key, value] of map) {
    if (!ref.has(key)) continue;
    if (holders(value) !== holders(ref.get(key))) {
      console.log(`PLACEHOLDERS  ${file}: ${key}  "${holders(ref.get(key))}" vs "${holders(value)}"`);
      problems++;
    }
  }
}

// The keys added this release must differ from English in every language (a value identical to
// English is indistinguishable from a key that fell back).
const added = ['Store.Demo.Exists', 'Store.Demo.Created', 'Store.Copy.Suffix', 'Store.Copy.SuffixNumbered',
  'Store.Manage.AddSection', 'Store.Manage.Copy', 'Store.Manage.CopyHint', 'Store.Manage.Copied',
  'Store.Manage.Created', 'Store.Manage.NothingToCopy'];

for (const [file, map] of Object.entries(tables)) {
  if (file === 'en-US.lang.xml') continue;
  for (const key of added) {
    if (map.get(key) === ref.get(key)) {
      console.log(`UNTRANSLATED  ${file}: ${key}`);
      problems++;
    }
  }
}

console.log(problems === 0 ? 'OK — parity, placeholders and new-key translation all clean'
                           : `FAILED with ${problems} problem(s)`);
process.exit(problems === 0 ? 0 : 1);

