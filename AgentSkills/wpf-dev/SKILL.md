---
name: wpf-dev
description: >-
  Practices for editing localized WPF (.NET) desktop apps that use an XML string
  table, EF Core SQLite with runtime column guards, per-section/service payment
  models, IValueConverter/IMultiValueConverter-driven XAML, and FlowDocument
  printing. USE WHEN: adding or wiring localized UI text; building/running a WPF
  exe that may be locked; keeping RadioButton/CheckBox groups in sync without
  reentrancy; adding computed money/summary fields; rendering repeating item
  lists with tiered borders; adding DataGrid row context menus, row keyboard
  shortcuts, or duplicating an aggregate record; sorting a paged list by clicking
  column headers; or generating printable
  receipts/documents. On
  every use, first classify the request: if it is a skill update, log it in
  SkillUpdates.md; otherwise read and maintain the companion TODO.md,
  context.md, and Architecture.md in the skill folder, checkpointing each new
  request to TODO.md before starting. When the chat session is running out of
  context (about to compact) or resuming after compaction, re-orient from
  Architecture.md first, then context.md for the last stored context, then
  TODO.md.
  DO NOT USE FOR: web frontends, non-WPF desktop stacks, or server code.
---

# WPF Localized-App Editing Practices

Reusable conventions for maintaining a localized WPF desktop app with an XML
string table, EF Core SQLite storage, converter-driven XAML, and print output.
Apply the specific ones that match the task; skip the rest.

## Who this skill is

`wpf-dev` is an **English-language full-stack WPF (.NET) developer**. It may
*converse* with the user in Chinese (or any language the user writes in), but
everything it **writes into the repository is English**:

- source code — identifiers, comments, XML-doc, log and exception messages;
- **Markdown** — every companion file (`TODO.md`, `context.md`,
  `Architecture.md`, `SkillUpdates.md`), including prose, findings and notes;
- commit messages and PR descriptions.

The **only** places non-English text is allowed:

1. `<Text>` values inside `Languages.xml` (and other explicit end-user data);
2. a verbatim quote of the user's own request in a `TODO.md` `- Ask:` line —
   that is a record of what was said, not prose;
3. naming the literal string-table **value** being changed, e.g.
   `reworded 尾款（余额）→剩余尾款` — quoting the data, not writing in it.

Everywhere else, refer to UI text by its **key** (`Order.Fields.FinalBalance`),
never by its Chinese label. Keys are stable and greppable; labels are neither.
Do not write a Chinese UI label as the subject of a sentence in a doc — write
the key and, if genuinely helpful, the value once in parentheses.

## 0. Session continuity & checkpoints (do this first)

The skill folder (same folder as this `SKILL.md`) holds two kinds of tracking
files:

- **Skill-only tracker:** `SkillUpdates.md` — a changelog of edits to the skill
  itself (this `SKILL.md`, its conventions, and the companion templates). It has
  **no relationship to any project**; it only records how the skill evolves.
- **Project companions:** `TODO.md` (open/checkpointed work), `context.md`
  (current project state, recent decisions, gotchas), and `Architecture.md`
  (component map). These preserve project state across sessions so work is never
  lost if a conversation is interrupted or compacted.

**On every use of this skill, first classify the request:**

### Step A — Is this a *skill update*?

A skill update = a request to change the skill itself: edit `SKILL.md`, add/adjust
conventions, rename the skill, or change the companion-file templates/format.

- **Yes → skill-update path:**
  1. Make the requested skill/companion changes.
  2. Append an entry to `SkillUpdates.md` (date + what changed + why).
  3. **Do not** touch project `TODO.md` for this — skill updates are not
     project tasks.

- **No → it's a project task → go to Step B.**

### Step B — Project task flow

1. **Read the project companions first** — `TODO.md`, `context.md`, and
   `Architecture.md` — before planning or editing code.
2. **Checkpoint the request:** compare the incoming ask to the last entry in
   `TODO.md`. If it is **not the same** as the last TODO (a genuinely new ask),
   append it as a new checkpoint entry (timestamp + verbatim ask + short task
   breakdown, status `PENDING`) **before** starting work. If it merely continues
   the last entry, update that entry instead of duplicating it.
3. **Keep them current as you work:** flip TODO items to `IN PROGRESS` / `DONE`;
   record notable decisions, new conventions, or gotchas in `context.md`; and
   update `Architecture.md` whenever you add/rename components or change how the
   pieces fit together.
4. If any tracking file is missing, create it (keep the same headings/format as
   the existing templates) rather than skipping the step.

### Step C — Resuming near/after a context-size limit (compaction)

When the chat session is running low on context (about to be summarized/compacted)
or you are picking up a conversation that was already compacted, **re-orient from
the companion files before doing anything else** — never trust a truncated
transcript alone:

1. **Read `Architecture.md` first** to rebuild the component map (what exists,
   how the pieces fit together, current names).
2. **Then read `context.md`** — specifically the newest entries under
   "Recent decisions / state" — to recover the *last stored context* (the most
   recent decisions, gotchas, and in-flight design).
3. **Then read `TODO.md`** to find the last checkpoint and its status, and resume
   there.
4. **Before context runs out**, proactively flush anything not yet written:
   append/refresh the `context.md` "Recent decisions / state" note and the
   `TODO.md` checkpoint status so the next (post-compaction) turn can recover
   cleanly. Treat these files — not the chat history — as the durable memory.

TODO checkpoint entry format:

```md
### <YYYY-MM-DD HH:mm> — <short title>  [PENDING|IN PROGRESS|DONE]
- Ask: "<verbatim user request>"
- Plan:
  - [ ] step 1
  - [ ] step 2
- Notes: <files touched, build result, follow-ups>
```

`SkillUpdates.md` entry format:

```md
### <YYYY-MM-DD> — <short title>
- Changed: <files / sections touched in the skill>
- Why: <reason / triggering request>
```

## 1. Localization via an XML string table

- **Source-code language is always English — no exceptions.** All identifiers
  (types, methods, fields, variables, parameters), code comments, commit-style
  notes, log messages, and companion-doc prose stay in English **even when the
  task is about adding or editing Chinese (or any non-English) UI text.** See
  "Who this skill is" above for the three narrow exceptions. Never put Chinese in
  identifiers, comments, or code literals — route every user-facing string
  through the string table key instead. Example: add the Chinese/English *values*
  under a new `<Text key="OrderEdit.ViewCustomMade">` and reference the **key** in
  code; do not hard-code `查看定制记录` in the `.cs`/`.xaml`.
- **Punctuation is part of the translation, not the code.** Chinese uses
  fullwidth `（）：、` where English uses `(): ,`. Never concatenate separators in
  C# around a localized fragment — put the whole line shape in the string table
  and fill it with `Format`, or English renders as `Alterations（Garment
  Adjustments）：$123`.
- **One key per meaning.** Before adding a label, check whether an existing key
  already names that value. Two keys bound to the same computed number is a bug:
  the same figure ends up called two different things depending on which panel is
  visible. Prefer reusing the existing key over inventing a stage-specific one.
- **Prune orphaned keys.** A key nothing references is dead weight that still has
  to be translated into every language block. Sweep for them by extracting every
  `<Text key="...">` and grepping the source for each. Mind the keys built by
  interpolation (`$"Measure.Term.{id}"`, `$"ClothingItem.{key}"`,
  `$"PaymentMethod.{method}"`, `$"OrderEdit.Panel.{enumValue}"`) — those are live
  even though no literal matches, so always check what values the interpolation
  can actually produce before deleting.
- UI text lives in a single source `Languages.xml` at the project root as
  `<Text key="Some.Key">value</Text>` entries. Each language is a full block of
  the same keys (e.g. Chinese entries first, English later in the file).
- **Only edit the root `Languages.xml`.** Copies under `bin/`, `publish/`, and
  `bin/Release/.../publish/` are build outputs — never edit those; they are
  overwritten on build.
- Bind in XAML with:
  `{Binding Source={x:Static loc:LocalizationService.Instance}, Path=[Some.Key], Mode=OneWay}`.
- In code-behind/converters, read with the indexer: `_localization["Some.Key"]`
  or `LocalizationService.Instance["Some.Key"]`; format with
  `_localization.Format("Key.With.{0}", arg)`.
- When adding a key, add it to **every** language block, keeping the same
  relative position so the file stays parallel and easy to diff.
- **Relabel vs. add:** to rename a label everywhere it is bound, change the
  key's *value* (not the key). To add a distinct label, add a new key.
- Fallback pattern (localize an enum/name, fall back to raw when missing):
  ```csharp
  var key = $"{prefix}.{suffix}";
  var localized = _localization[key];
  return string.Equals(localized, key, StringComparison.Ordinal) ? suffix : localized;
  ```

## 2. Building/running a WPF app whose exe gets locked

The running app locks its exe, so a build fails with a file-in-use error unless
you stop it first. Always kill, pause briefly, then build:

```powershell
Get-Process -Name <AppName> -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400
dotnet build <Project>.csproj -v quiet --nologo 2>&1 | Select-String -Pattern "error|Build succeeded|Build FAILED"
```

Expect `Build succeeded. 0 Error(s)`. Verify after every change set.

## 3. EF Core SQLite with runtime column guards

- Schema is evolved at startup with idempotent `ALTER TABLE ... ADD COLUMN`
  guards (check existing columns first) rather than only migrations. When you
  add a persisted model property, add a matching guard so existing databases
  upgrade in place, e.g.:
  ```csharp
  if (!columns.Contains("NewColumn"))
      await db.Database.ExecuteSqlRawAsync("ALTER TABLE Orders ADD COLUMN NewColumn TEXT NULL; ");
  ```
- Computed values belong on the model as `[NotMapped]` properties, and must be
  `entity.Ignore(...)`-d in `OnModelCreating` if EF would otherwise map them.

## 4. Per-section / per-service money model

When an entity aggregates several independently-priced sections (services):

- Store each section's inputs separately (`XxxSubtotal`, `XxxTaxRate`,
  `XxxDownpayment`, `XxxBalanceCleared`, and a payment method **per portion** —
  one for the deposit, one for the final balance).
- **Tax is applied per payment portion, not per section.** The entered deposit
  and final balance are **pre-tax** base amounts; each portion is taxed only when
  **that portion's** method is Card. Compute the whole split once in a single
  static function returning an immutable `readonly record struct`, and have every
  per-section computed property delegate to it so the model and the live editor
  never diverge:
  ```csharp
  public readonly record struct SectionPayment(
      decimal Subtotal, decimal Deposit, decimal FinalBase,
      decimal ReceivedDownpayment, decimal FinalCharge, decimal Total, decimal Tax);

  public static SectionPayment CalculateSectionPayment(
      decimal subtotal, decimal deposit, decimal ratePercent,
      PaymentMethod? downMethod, PaymentMethod? finalMethod)
  {
      var safeSubtotal = subtotal < 0m ? 0m : subtotal;
      var safeDeposit  = Math.Clamp(deposit, 0m, safeSubtotal); // deposit is PRE-TAX
      var finalBase    = safeSubtotal - safeDeposit;
      var rate         = ratePercent < 0m ? 0m : ratePercent;
      var depositRate  = downMethod  == PaymentMethod.Card ? rate : 0m;
      var finalRate    = finalMethod == PaymentMethod.Card ? rate : 0m;
      var recvDown     = safeDeposit + safeDeposit * depositRate / 100m;
      var finalCharge  = finalBase   + finalBase   * finalRate   / 100m;
      return new SectionPayment(safeSubtotal, safeDeposit, finalBase, recvDown,
          finalCharge, recvDown + finalCharge,
          (recvDown - safeDeposit) + (finalCharge - finalBase));
  }
  ```
  This generalizes the older "any card taxes the whole section" rule: when both
  portions share a method the totals are identical, so persisted extremes are
  preserved.
- Expose `[NotMapped]` accessors per section (`XxxMoney => CalculateSectionPayment(...)`)
  and aggregate off them: `XxxTotal => XxxMoney.Total`, `XxxTax => XxxMoney.Tax`,
  `ReceivedDownpayment = Σ XxxMoney.ReceivedDownpayment` (实收定金),
  `TotalTax = Σ XxxTax`. Nominal deposit total (`TotalDownpayment`) stays the sum
  of the pre-tax deposits.
- Model "cleared" / "residual" / "received-final" with small static helpers that
  take the struct, so UI and model agree:
  ```csharp
  static bool    IsSectionCleared(SectionPayment m, bool cleared)
      => m.Total <= 0m || cleared || m.FinalBase <= 0m;
  static decimal SectionResidual(SectionPayment m, bool cleared)
      => (cleared || m.FinalBase <= 0m) ? 0m : m.FinalCharge;   // taxed final still owed
  static decimal SectionReceivedFinal(SectionPayment m, bool cleared)
      => (cleared && m.FinalBase > 0m) ? m.FinalCharge : 0m;
  ```
- `FinalBalance` (剩余尾款) = Σ residuals; `ReceivedFinalBalance` (实收尾款) = Σ
  received finals. The live editor must call the **same** `CalculateSectionPayment`
  and compare "fully paid / cleared" against the **pre-tax subtotal base** (never
  the taxed total), so persisted and on-screen values match. Persisted
  `TotalAmount` is recomputed on save; legacy mixed-method rows keep their stored
  total while the breakdown recomputes.
- When surfacing the split, show the nominal vs. received amounts **as a pair**
  (定金/实收定金 on one row, 剩余尾款/实收尾款 on the next) and, in any per-section
  breakdown text, show each portion's base amount **and** its card tax
  (`ReceivedDownpayment − Deposit`, `FinalCharge − FinalBase`). On a receipt, only
  print the received-deposit line when it differs from the nominal deposit.

## 5. RadioButton/CheckBox sync without reentrancy

Programmatically changing controls fires their own event handlers, causing
loops. Guard with a bool flag:

```csharp
private bool _syncing;

private void OnSomethingChanged(object sender, RoutedEventArgs e)
{
    if (_syncing) return;
    _syncing = true;
    try { /* set other controls programmatically */ }
    finally { _syncing = false; }
    RefreshComputedTotals(runAutoComplete: false); // recompute AFTER releasing the guard
}
```

- To reflect derived state back onto a "master" control inside a refresh method,
  wrap just that assignment in the guard so it does not re-trigger the handler.
- A "clear all" master checkbox should iterate charged sections only
  (`sectionTotal > 0`), set each section's cleared flag, and mirror each
  section's final-payment method from its deposit method.
- Radio groups switch exclusive panels via `Visibility`; hidden panels keep
  their in-memory values in WPF and are still saved — so multi-section data
  survives even when only one section is visible at a time.
- **Default selection for dropdowns/pickers:** when adding a `ComboBox` (or any
  single-select picker) and the request does **not** specify a default, always
  pre-select the **first** option rather than leaving it blank — on the new /
  setup path set `SelectedIndex = 0`, and on the edit-load path fall back to the
  first item when the stored value matches none. Only leave it unselected when
  explicitly asked.

## 6. Converter-driven XAML

- `IValueConverter` for one input → display string/visibility (e.g. null →
  `Visibility`, enum → localized text, order → summary string).
- `IMultiValueConverter` when the result depends on the item **and** its
  context (see the last-item border trick below).
- Register in `<Window.Resources>` with an `x:Key`, reference with
  `{StaticResource Key}`. Converters can call `LocalizationService.Instance`
  directly for localized output.

## 7. Repeating lists with tiered, clean borders

Standardize a 3-tier visual hierarchy for nested sections:

- **Darkest, thicker** border = major section split (e.g. 2px `#8A929B`).
- **Medium, thin** border = per-service section (e.g. 1px `#C7CDD4`).
- **Lightest, thin** border = between individual items (e.g. 1px `#EEE`).

Show a divider **between** items but not after the last one using an
`IMultiValueConverter` bound to the item and the owning `ItemsControl.Items`:

```csharp
public object Convert(object[] values, Type t, object? p, CultureInfo c)
{
    if (values.Length < 2 || values[1] is not IEnumerable items) return new Thickness(0,0,0,1);
    object? last = null; foreach (var e in items) last = e;
    return Equals(values[0], last) ? new Thickness(0) : new Thickness(0,0,0,1);
}
```
```xml
<Border.BorderThickness>
    <MultiBinding Converter="{StaticResource LastItemBorderThickness}">
        <Binding/>
        <Binding RelativeSource="{RelativeSource AncestorType=ItemsControl}" Path="Items"/>
    </MultiBinding>
</Border.BorderThickness>
```

Collapse an empty section cleanly by putting the `Visibility` trigger on the
**same** element that carries the border (wrap the section in a `Border` whose
`Style` has a `DataTrigger` on `...Items.Count == 0` → `Collapsed`), so a hidden
section never leaves a dangling divider line.

## 8. Printable receipts/documents (FlowDocument)

Build a `FlowDocument` in code and print via `PrintDialog`:

```csharp
var pd = new PrintDialog();
if (pd.ShowDialog() != true) return;
var doc = BuildDocument(model, pd.PrintableAreaWidth); // set PageWidth/ColumnWidth = width
pd.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, title);
```

- Compose with `Paragraph`/`Run`/`Bold`, small helper methods for label lines,
  section titles, and divider paragraphs (a `Paragraph` with a bottom
  `BorderThickness`).
- Localize all text through the string table; **include only sections that are
  actually "added"** — i.e. have data or a charge, and (when the domain requires
  it) a **deposit/payment method selected**. Express that gate once as a
  `[NotMapped] bool XxxAddedToReceipt` on the model (e.g.
  `XxxTotal > 0m && XxxDownpaymentMethod is not null`) and **reuse the same
  property for both** the printed receipt (skip the section when false) **and**
  the on-screen detail panel (bind the section `Border.Visibility` through the
  built-in `BooleanToVisibilityConverter`). Keeping one gate avoids the classic
  bug where the receipt hides a zero/priceless section but the detail panel still
  shows it (or vice-versa). Report success/failure to a status bar with
  localized, formatted messages inside a try/catch.

## 9. Workflow discipline

- Read a file region before editing; make minimal, targeted edits with enough
  surrounding context to match uniquely; batch independent edits together.
- **Sanity-check BOTH gates BEFORE building — IDE diagnostics *and* Sonar.**
  After a change set, do not jump straight to `dotnet build`. Run a two-part
  pre-build sanity check on every changed file first (see section 9b), then and
  only then kill + build + confirm `Build succeeded. 0 Error(s)`. Building first
  hides that (a) the editor may still show real compiler errors, (b) the IDE may
  show stale Roslyn false positives that need a language-server refresh, not a
  code change (see section 15), and (c) Sonar smells that a green build ignores.

### 9b. Pre-build sanity check (two gates)

Run these **in order, before every build**, on each changed file:

1. **Gate 1 — IDE / Roslyn diagnostics.** Read the editor diagnostics for each
   changed file (`get_errors`). Classify each entry by its *owner/source*:
   - **Real compiler error** (genuine `CSxxxx` that the build will also report):
     fix the code.
   - **Roslyn / C# Dev Kit false positive** — `CS0103`
     "... does not exist in the current context" on `x:Name` controls or
     `InitializeComponent`, owner `DocumentCompilerSemantic`, origin `extHost1`,
     while `dotnet build` is clean. This is a **stale design-time model**, not a
     code bug: **restart the C# language server** and re-check — do **not** edit
     correct code or add `[SuppressMessage]` (see section 15).
   - **SonarLint issue:** handle in Gate 2, not here.
   Gate 1 passes when every changed file reports **zero** diagnostics that are
   real or that survive a language-server restart.
2. **Gate 2 — SonarQube (SonarLint).** Analyze each changed file and clear every
   flagged issue at every severity/category (see sections 10, 9a). Re-analyze to
   confirm the file is 100% Sonar-clean.

Only after **both** gates are green: kill the exe + `dotnet build` + confirm
`Build succeeded. 0 Error(s)`. Running the sanity check first keeps quality gates
ahead of the build and separates a *stale-IDE* false positive (fixed by a server
restart) from a *real* code defect (fixed by editing).
- **SonarQube is ZERO TOLERANCE — ANY SonarQube warning, alert, issue, or error
  on a changed file MUST be fixed before the task is considered done, at every
  severity (Blocker / Critical / Major / Minor / Info, and every category: Bug,
  Vulnerability, Security Hotspot, Code Smell).** A clean build is *not* enough:
  a task is only complete when the changed files are 100% Sonar-clean — zero
  warnings, zero alerts, zero errors. Do not defer, downgrade, batch-for-later,
  or hand-wave a flag ("minor", "info", "optional", "style", "pre-existing" are
  all in scope; if you touched the file, you own its Sonar state). After fixing,
  **re-analyze the file to confirm zero remaining issues**. The only acceptable
  non-fix is a genuine, context-invalid false positive (see section 10) — and
  that must be either restructured to resolve cleanly or suppressed with a
  justified `[SuppressMessage]`; never left silently outstanding.
- Actively resolve genuinely flagged Sonar issues instead of ignoring them;
  only leave documented false positives.
- Do not create Markdown docs to describe changes unless explicitly asked.

### 9a. Write clean the first time (avoid these while coding — Sonar + IDE)

These four smells recur constantly in this codebase — **do not write them and
then fix them; avoid them as you type.** Full remediation detail is in section 10.
Beyond Sonar, also avoid the coding moves that trip the **Roslyn / C# Dev Kit
language server** into stale-model false positives (full detail in section 15):
after you **add or rename a XAML window / add new `x:Name` controls**, expect the
editor to briefly show `CS0103` on those generated members until the IDE
re-runs its design-time build — **build once, then restart the C# language
server** so the model regenerates the design-time partials; never "fix" those
CS0103s by editing the (correct) code or adding a suppression.

- **Cognitive complexity too high (S3776, keep ≤ 15).** Do not grow a method
  into a long `if`/`foreach` ladder. As soon as a builder/handler gains a few
  independent branches or sections, split it into small, single-purpose
  helpers (e.g. one `AddXxxSection(...)` per receipt section) or drive it from a
  data table + loop. Prefer early-return guards over nested blocks.
- **Non-instance members should be static (S2325).** If a method/property does
  not touch instance state (`this`, fields, instance members), declare it
  `static` from the start. **Exception — WPF code-behind that only reads
  `x:Name` controls:** SonarLint's single-file pass can't see the generated
  fields and mis-flags it; keep that logic **inline** in the instance method
  (constructor / event handler) instead of extracting a helper, so the false
  positive never appears.
- **No redundant boolean literals (S1125).** Never write `x == true`,
  `x != false`, or `ShowDialog() == true`. Use `x is true` / `x is not true` /
  `ShowDialog() is true`. For a `bool?` consumed as a bool (ternary/if
  condition, bool assignment/argument) use `.GetValueOrDefault()` — Sonar flags
  even `is true` there.
- **No nested ternaries (S3358).** Never chain `a ? x : b ? y : z`. Write an
  `if` / `else if` / `else` block (or a `switch` expression) instead.

## 10. SonarQube (SonarLint) code-quality cleanup

**Zero tolerance:** any SonarQube warning, alert, issue, or error on a changed
file must be fixed before the work is done — regardless of severity label
(Blocker → Info) or category (Bug / Vulnerability / Security Hotspot / Code
Smell). "Build succeeded" does not close a task; "changed files are 100%
Sonar-clean" does. Re-analyze after each fix until the file reports nothing.

Workflow: **run SonarQube (SonarLint) analysis before the build.** After a
change set, trigger analysis per changed file first, then read the results from
the Problems view; fix only genuinely flagged items and re-analyze to confirm
the file is Sonar-clean. Only after that, kill + build to confirm
`Build succeeded. 0 Error(s)`. Flags are inconsistent — only some occurrences of
a pattern get reported, so fix what is actually flagged and do not
blanket-change unflagged code.

Caveat: SonarLint's standalone (single-file) analysis cannot see XAML-generated
fields (e.g. `x:Name`d controls in the `.g.cs`). A method that only touches such
generated members can be mis-flagged (e.g. S2325 "make static"). When that
happens, prefer inlining the one-off logic or otherwise referencing resolvable
instance state rather than complying with the false positive; a full build
regenerates the code-behind so a re-analysis after building can also confirm.

Concrete rule fixes observed in this codebase:

- **S1125 (redundant boolean literals):** replace `x == true` / `x != true`
  with `x is true` / `x is not true`, and `ShowDialog() == true` with
  `ShowDialog() is true`. On a nullable `bool?` whose value is *consumed*
  (ternary condition, bool assignment, bool argument), Sonar still flags
  `is true`; use `.GetValueOrDefault()` there (behaviorally identical for
  `bool?`).
- **S3776 (cognitive complexity ≤ 15):** decompose the method; prefer a
  data-driven table + loop over long `if`-ladders. Example: replace ~30
  sequential `if (!columns.Contains("X")) ExecuteSqlRaw(...)` column guards with
  a `static readonly (string Column, string Ddl)[]` table iterated in a loop,
  and extract schema-reading into small helpers.
- **S107 (too many parameters, > 7):** group related parameters into a
  `readonly record struct` (e.g. a per-section payment struct) and pass that
  instead. Splitting into smaller methods is the other option.
- **S2325 (make static):** make helpers/GraphQL resolvers that use no instance
  state `static`. **False positives to NOT force static:**
  - EF `DbSet` properties — declare them as auto-properties
    `public DbSet<T> Xs { get; set; } = null!;` (carries instance state, clears
    the rule) rather than `=> Set<T>()`.
  - A method implementing an interface (e.g.
    `IDesignTimeDbContextFactory<T>.CreateDbContext`) — cannot be static;
    suppress with justification.
  - A WPF-bound view-model property (`{Binding X}`) — must stay instance;
    suppress with justification.
  - Suppress via
    `[SuppressMessage("Minor Code Smell", "S2325:...", Justification = "...")]`
    (`using System.Diagnostics.CodeAnalysis;`). SonarLint honors it.
- **S3604 (redundant member initializer):** if a field like
  `private bool _isInitializing = true;` needs its value during
  `InitializeComponent()`, remove the initializer and set it explicitly as the
  first constructor statement to preserve behavior.
- **S3358 (nested ternary):** extract nested `?:` into `if`/`else if`/`else`
  blocks or a guarded block body.
- **S3267 (loop should be a LINQ call):** when a `foreach` body only filters
  and/or projects (e.g. every use of the loop variable is `item.Id`), Sonar asks
  you to express it with `Where`/`Select`. Either iterate the projected sequence
  directly (`foreach (var id in items.Select(i => i.Id))`) or add the
  `Where(...)` filter — do not keep the manual loop just because it works.
- **S3878 (redundant array in params call):** pass elements directly, e.g.
  `string.Join(" | ", a, b)` instead of `string.Join(" | ", new[] { a, b })`.
- **S1075 (hardcoded absolute URI):** extracting the full URL to a single
  `const` does **not** clear it. Compose the URL from separate parts
  (`ServerScheme` / `ServerHost` / `ServerPort` constants →
  `$"{ServerScheme}://{ServerHost}:{ServerPort}"`) so no literal contains a full
  scheme+authority URI. Reading from config/env is an alternative.
- **S2077 (string-formatted SQL):** never interpolate into `CommandText`. Use
  parameters (`command.CreateParameter()` with `@name`) for values; for
  fixed-shape statements that cannot take parameters (e.g.
  `PRAGMA table_info('Orders')`), pass a literal SQL string rather than
  interpolating a table name.

General principle: distinguish real issues from context-invalid false positives
(EF `Set<T>()`, design-time factory interface, XAML-bound VM members). Fix the
real ones; for the rest use the auto-property form where possible, otherwise a
justified `[SuppressMessage]`.

## 11. DataGrid row context menus, commands & keyboard shortcuts

Right-click menus, row selection, and keyboard actions on the order list.

- **Do NOT put `Click=`/event handlers on elements inside a Style `Setter.Value`.**
  A `ContextMenu` (and its `MenuItem`s) placed in a `DataGridRow` style's
  `Setter Property="ContextMenu"` is a shared/templated value that is **not**
  connected to the code-behind, so `Click="OnX"` fails to compile with a
  *mis-attributed* `MC6007` error (e.g. "`Click` is not an event on
  `DataGridTextColumn`", pointing at the wrong line). Instead attach the menu
  **directly on the control** as `<DataGrid.ContextMenu><ContextMenu>...` — there
  its `MenuItem Click`/`Command` wire up normally. Keep the row `Style` for
  `EventSetter`s only.
- **Right-click does not select a DataGrid row** by default, so a shared
  `DataGrid.ContextMenu` would act on the previously-selected row. Select the
  row first via an `EventSetter` in the row style:
  ```xml
  <DataGrid.RowStyle>
    <Style TargetType="DataGridRow">
      <EventSetter Event="PreviewMouseRightButtonDown" Handler="OnRowRightClick"/>
    </Style>
  </DataGrid.RowStyle>
  ```
  ```csharp
  private void OnRowRightClick(object sender, MouseButtonEventArgs e)
  { if (sender is DataGridRow row) row.IsSelected = true; }
  ```
  (`EventSetter` inside a `Style` *is* the supported way to hook events — only
  `Click` on `Setter.Value` content is the problem.)
- **Route menu items to existing paths, one code path per action.** Reuse the
  same `ICommand`s the toolbar uses (`if (_vm.XCommand.CanExecute(null))
  _vm.XCommand.Execute(null);`) or delegate to the existing `Click` handler
  (`OnContextEditClick → OnEditOrderClick`). Don't duplicate logic in the menu.
- **Keyboard shortcuts on the grid:** handle `Enter` (open/show details) and
  `Delete` in a single `KeyDown` switch; send `Delete` through the **same**
  confirm-dialog command as the toolbar/menu so the guard is never bypassed:
  ```csharp
  switch (e.Key) {
      case Key.Enter:  e.Handled = true; OnEditOrderClick(sender, new()); break;
      case Key.Delete: e.Handled = true;
          if (_vm.DeleteOrderCommand.CanExecute(null)) _vm.DeleteOrderCommand.Execute(null);
          break;
  }
  ```
- **Destructive commands own their confirmation.** Put the `MessageBox`
  Yes/No confirm inside the command/method itself (not the call sites) so every
  trigger — toolbar, context menu, and `Delete` key — is covered by one dialog.

## 12. Duplicating an aggregate record ("Copy order")

To copy an entity that owns child rows and derived state:

- Load the source **`AsNoTracking()`** with its children `Include`d, then build a
  brand-new instance copying every persisted scalar; **do not** carry the `Id`.
- Give it a fresh natural key and timestamp (e.g.
  `OrderNumber = $"ORD-{DateTime.Now:yyyyMMdd-HHmmss}"`, `OrderDate = UtcNow`) —
  reuse the same generator the new-record path uses.
- Deep-copy child collections as **new** rows (new `OrderItem { ... }` without
  `Id`) so EF inserts them rather than re-parenting the originals.
- **Reset "closed" status on copy:** if the source status is a finished one
  (`Completed`/`Cancelled`/`Returned`), set the copy to the active default
  (`Processing`); otherwise keep the source status. Because status-derived flags
  (e.g. the "已取货 / picked up" tick == `Status == Completed`) have no own
  column, resetting the status also clears them automatically.
- Save, reload the list, then re-select the copy by its new `Id`; report a
  localized, formatted status message.

## 13. Sortable column headers on a paged GridView/ListView

Click a `GridViewColumnHeader` to sort ascending, click again to flip to
descending. Because the list is **paged in the view-model** (`Orders` holds only
the current page), sort the whole filtered set in the VM before paging — never
via `ListView.Items.SortDescriptions` (that would sort one page only).

- **Declare the sort member per column with an attached property** rather than
  matching on the localized header text (which changes with language). A tiny
  static owner class carries both the sort key and the header arrow glyph:
  ```csharp
  public static class OrderColumnSort
  {
      public static readonly DependencyProperty SortKeyProperty =
          DependencyProperty.RegisterAttached("SortKey", typeof(string),
              typeof(OrderColumnSort), new PropertyMetadata(string.Empty));
      public static void   SetSortKey(DependencyObject o, string v) => o.SetValue(SortKeyProperty, v);
      public static string GetSortKey(DependencyObject o) => (string)o.GetValue(SortKeyProperty);
      // SortGlyph (string) registered the same way — holds " ▲" / " ▼" / "".
  }
  ```
  ```xml
  <GridViewColumn local:OrderColumnSort.SortKey="OrderNumber" Header="..."/>
  ```
- **Handle the header click once on the ListView** via the bubbled
  `GridViewColumnHeader.Click` attached event; skip the padding header:
  ```csharp
  if (e.OriginalSource is not GridViewColumnHeader h
      || h.Role == GridViewColumnHeaderRole.Padding || h.Column is null) return;
  var key = OrderColumnSort.GetSortKey(h.Column);
  if (!string.IsNullOrEmpty(key)) { _viewModel.SortBy(key); UpdateSortGlyphs(); }
  ```
- **VM owns sort state** (`_sortKey`, `_sortAscending`). `SortBy(key)` toggles
  direction when the same key is clicked else resets to ascending, then rebuilds
  the view. Apply it in the rebuild **after filtering, before Skip/Take paging**,
  with a data-driven key selector so mixed types compare correctly:
  ```csharp
  private static Func<Order, object?>? GetSortSelector(string k) => k switch {
      nameof(Order.OrderNumber) => o => o.OrderNumber ?? string.Empty,
      nameof(Order.OrderDate)   => o => o.OrderDate,
      nameof(Order.Status)      => o => (int)o.Status,
      nameof(Order.TotalAmount) => o => o.TotalAmount,
      "BalanceStatus"           => o => o.IsBalanceCleared,
      _ => null };
  // filtered = asc ? filtered.OrderBy(sel).ToList() : filtered.OrderByDescending(sel).ToList();
  ```
  Coalesce nullable strings/dates in the selector so `Comparer<object>.Default`
  never sees null; sort enums by their `int` value.
- **Header arrow indicator:** give the header style a `ContentTemplate` whose
  arrow `TextBlock` binds to the column's attached glyph, reaching the column
  through the header via `RelativeSource`:
  ```xml
  <TextBlock Text="{Binding Column.(local:OrderColumnSort.SortGlyph),
             RelativeSource={RelativeSource AncestorType=GridViewColumnHeader}}"/>
  ```
  After each sort, `UpdateSortGlyphs()` walks `GridView.Columns` and sets each
  column's `SortGlyph` to the arrow when its key matches the active sort, else "".

## 14. Status-dependent labels & gated quick-actions on the list

- **Read-only status → relabel the open action.** When an order's status is a
  read-only one (`Completed`/`Cancelled`/`Returned`), the toolbar button **and**
  the row context-menu item should read "查看订单 / View Order" instead of
  "编辑订单 / Edit Order". Drive both from one `RefreshToolbarLabels()` that sets
  `EditOrderButton.Content` and the named `EditContextMenuItem.Header` to the same
  localized key; call it on `SelectedOrder` change and on `LanguageChanged`.
  Right-click already selects the row (via the `PreviewMouseRightButtonDown`
  `EventSetter`), so the menu reflects the right-clicked order.
- **Gate a quick-complete checkbox on real state.** The "已取货 / picked up"
  checkbox must be un-checkable until the order actually has a charge **and**
  every final balance is cleared. Because the order-cleared predicate already
  returns false when the total is zero, one condition covers both:
  `PickedUpCheck.IsEnabled = cleared || PickedUpCheck.IsChecked.GetValueOrDefault();`
  (keep it enabled while already ticked so a completed order can be reverted;
  read-only orders stay fully locked).

## 15. Roslyn / C# Dev Kit language-server false positives (IDE, not Sonar)

The VS Code editor runs **two independent analyzers** on this project: the
**Roslyn / C# Dev Kit language server** (compiler-style diagnostics, `CSxxxx`)
and **SonarLint** (code smells, `Sxxxx`). They fail differently and are fixed
differently — never conflate them. This section is about the Roslyn side; Sonar
is sections 9a/10.

### How to recognize a Roslyn stale-model false positive

- Error text: **`CS0103` "The name '<X>' does not exist in the current context"**
  (or similar `CS0246`/`CS1061`) pointing at an **`x:Name`d control**, a
  generated field, or **`InitializeComponent()`** in a `*.xaml.cs`.
- Diagnostic metadata: **`owner`/source `DocumentCompilerSemantic`**, origin
  `extHost1` — i.e. the C# language server, **not** `sonarlint` and **not** the
  build.
- The tell-tale: **`dotnet build` is clean (`0 Error(s)`)** yet the editor still
  shows dozens of these. A correct build + red editor = stale IDE model.
- Typically appears right after **adding a new XAML window, renaming one, or
  adding new `x:Name` controls** — the IDE's design-time build hasn't
  regenerated that window's partial (`.g.cs` / design-time `.g.i.cs`) yet, so
  Roslyn can't see the generated `x:Name` fields or `InitializeComponent`.

### Why it happens

WPF splits each window into your `*.xaml.cs` **and** a generated partial that
declares the `x:Name` fields and `InitializeComponent()`. `dotnet build`
produces the build-time `*.g.cs`; the IDE uses its **own design-time build**.
When the language server's project model is stale (new/renamed file, or it
didn't re-run the design-time pass), those generated members are missing from
*its* view only → `CS0103`. The code is correct; the model is stale.

### The fix — refresh the model, do NOT touch the code

1. Confirm it is stale-model, not real: run a clean `dotnet build` — if it
   reports `0 Error(s)`, the CS0103s are false positives.
2. **Restart the C# language server** (command `dotnet.restartServer`, a.k.a.
   "C#: Restart Language Server") so it re-runs the design-time build and
   regenerates the partials. Prefer this over a full window reload (lighter, and
   a reload restarts the extension host).
3. Re-check editor diagnostics (`get_errors`) on the affected files — they clear
   once the model refreshes.
4. **Never** "resolve" these by editing the correct code, renaming controls, or
   adding `[SuppressMessage]` — compiler diagnostics from a stale model are not
   suppressible and there is nothing wrong to fix. Suppression/edits only mask
   the real fix (a model refresh) and risk breaking working code.

### Avoid it proactively while coding

- After adding/renaming a XAML window or adding `x:Name` controls: **build once**
  to generate the partials, **then restart the language server** before trusting
  the editor's red squiggles.
- Do **not** try to force-generate the design-time IntelliSense files from the
  CLI (`dotnet msbuild -t:MarkupCompilePass1 -p:DesignTimeBuild=true` fails
  standalone — it needs the IDE's reference-assembly graph). The language-server
  restart is the supported refresh.
- Standalone SonarLint can't see these generated fields either (section 10
  caveat) — but that is a *separate* tool; don't fix a Roslyn CS0103 with a Sonar
  workaround or vice-versa. Diagnose by the diagnostic's owner/source first.
