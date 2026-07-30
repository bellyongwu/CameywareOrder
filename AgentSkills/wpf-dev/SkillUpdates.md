# Skill Updates — wpf-dev

Changelog for the **skill itself** (this `SKILL.md`, its conventions, and the
companion-file templates). This file has **no relationship to any project** — it
only tracks how the `wpf-dev` skill evolves. Add a new entry at the top whenever
the skill is changed.

Entry format:

```md
### <YYYY-MM-DD> — <short title>
- Changed: <files / sections touched in the skill>
- Why: <reason / triggering request>
```

### 2026-07-30 — The CJK verification grep now covers Markdown, and says how to sort the hits
- Changed: `SKILL.md` "Who this skill is" — the grep's `-Include` widened to `*.md,*.json,*.csproj,*.ps1`
  alongside `*.cs,*.xaml`, its character class made explicit, and a new paragraph on triaging hits.
- Why: user asked whether any Chinese comments remained in the project. Source was clean; the
  **companions were not** — roughly 310 lines. The rule has always covered Markdown explicitly, but the
  grep added to enforce it globbed only `*.cs,*.xaml`, so it reported clean every time it ran. A
  verification narrower than its rule does not merely miss things, it certifies them.
- Also recorded, because each was a wrong first attempt: the sweep must be a **whitelist of known
  labels** (a bare-token pass left a key spliced onto a trailing Chinese fragment, since short tokens
  are substrings of compounds); most hits are **sanctioned** and a regex cannot tell, so sort by
  what the Chinese is doing — naming a UI surface (violation) vs naming a string-table value or quoting
  the user (keep); and verbatim quotes appear outside `- Ask:` lines, so protect on the quote character.
- The character class must NOT be widened to curly quotes: French `l’` is U+2019 and every fr-FR line
  would match.

### 2026-07-30 — §4b: reporting a refused save
- Changed: `SKILL.md` — new §4b between the money model and the reentrancy section.
- Why: a request to "modularize the error message box" exposed a form with eleven validation checks and
  no rule behind how any of them reported — five raised a dialog, two wrote a message under their
  field, and the summary line sat at the foot of a form taller than the window. The three surfaces
  (dialog / banner / inline) each answer a different question and a refusal needs all three.
- Also recorded: collect the blank fields in one pass rather than failing fast (two missing fields are
  two facts), and keep the dialog in ONE wrapper so the marking half stays testable — a `MessageBox`
  inside a check blocks the thread and hangs any harness that drives Save.

### 2026-07-29 — §4a: adding a second pricing mode (tax-inclusive vs tax-exclusive)
- Changed: `SKILL.md` — new §4a under the money model; §4's breakdown bullet now says to read the
  per-portion tax off the struct rather than re-derive it, and its Chinese label names replaced with
  keys (the rule in "Who this skill is" applies to this file too, and had eroded here).
- Why: reviewing a tax-jurisdiction change set surfaced three failures that are general, not
  project-specific — an optional mode parameter that silenced the compiler and let one caller keep the
  old arithmetic; every consumer deriving tax as `Received − Deposit`, which is structurally zero once
  tax is embedded, so a receipt printed "tax 0" beside a non-zero total; and labels that only make
  sense in one of the two modes (`subtotal + tax = total` does not hold in the other).
- Also recorded: an inclusive rate cannot come from a per-payment-method table, because a value-added
  tax is a property of the sale rather than of the tender.

### 2026-07-29 — "Who this skill is": the user's language never sets the code's language
- Changed: `SKILL.md` — "Who this skill is" rewritten and given teeth.
- Why: user instruction — "now remove all chinese words comments from all places",
  narrowed to "Just keep English comments across the application", then: "you need to
  add this into SKILL as well, even though I communicate with you by Chinese, you still
  need to use English to develop everything".
- The rule was already there and was still broken **62 times across 25 files**. So the
  edit is about why it eroded, not about restating it:
  - Separated the two audiences explicitly. Answering the user in Chinese is a courtesy
    to *one* reader; the repository serves every future reader, including ones who do
    not read that language. Conflating the two is how each individual lapse felt
    reasonable at the time.
  - Named the specific trap: **a task that is *about* Chinese text is not licence to
    comment in Chinese.** Almost every violation was of this kind — a comment naming a
    menu or a field by its Chinese label while describing perfectly ordinary logic.
  - Recorded the failure mode honestly: this rule "erodes quietly", because each comment
    looks fine in isolation and only the accumulation is visible. Included the actual
    count, since a number is harder to wave away than "be careful".
  - Added a **grep to run before finishing**. A rule with no check is a preference; this
    one now has a one-command verification, which is the only reason to expect the
    section to still be true in a month.
  - Widened exemption 3 to cover quoting a language's *punctuation* to describe it
    (`（）` against `( )`) — that came up immediately in the sweep and is data, not prose.

### 2026-07-29 — §1a: report per-language values in EVERY language, not just the new one
- Changed: `SKILL.md` §1a — one bullet added to the "a new language is a DATA task" list.
- Why: adding ja-JP straight after es-ES. The seeder was changed to print each record's
  name in every shipped language rather than only the one just added, and that
  immediately exposed a record that had been showing its **Chinese** name to French
  readers ever since fr-FR shipped. Nobody had noticed, because the fallback renders
  something — which is exactly the failure mode the bullet now names.
- Also records the limit: **report, do not assert.** A user-created record may legitimately
  carry only one language, so a "every record has every name" test would go red on correct
  data. The value is in looking, not in failing.

### 2026-07-29 — Language add/removal has a fixed, narrow test scope
- Changed: `SKILL.md` §1 — new subsection **1a. Adding or removing a language — what to
  test, and what NOT to**.
- Why: user instruction after adding es-ES — "For lanuage add/removal, just need to test
  if keys are added identical, plus the translation is percise. no need to rerun and
  retest the whole application. but needs to test if all lanauges are deleted."
- What it now says, and why each part is worth writing down:
  - **The scope is three checks, not a regression sweep.** Adding a language is a data
    change to a discovered folder; re-running an entire suite for it costs a lot and
    proves nothing the three checks do not. Naming the scope is the point — without it
    the default is "run everything", which is what the instruction was correcting.
  - **Key parity** and **translation precision** were already practised on this project;
    they are now stated as the *required* pair rather than as things that happened to be
    done. The precision check needs the cognate rule beside it or it produces false
    alarms the first time a language legitimately spells a word the English way.
  - **A cognate exemption is keyed on (key, language)**, never on the
    shared-in-every-language list. That distinction is the part most likely to be got
    wrong, because the all-languages list is right there and "works". It would silently
    exempt the same key in every *other* language too.
  - **All languages deleted** is the case the user specifically asked for, and it had no
    coverage at all. Writing the requirement as "fail loudly and name the cause" rather
    than "handle it" matters: there is no graceful degradation available — an app with no
    string table cannot even say what went wrong in the user's language.
  - Two traps recorded because they are only visible on these paths: whether a load guard
    sits **before** or **inside** the parse decides if a failed reload leaves the old
    table intact or blanks it; and every *stored* language code is a reference that can
    outlive its file.
  - **Never hard-code a language count in a test.** Generalised from a real cost: a
    `Count == 3` assertion made the fourth language fail a test that had nothing to say
    about it — the exact coupling that discovery exists to remove.

### 2026-07-28 — RefinedTODO.md: read a condensed history, keep the full one
- Changed: `SKILL.md` §0 (new `RefinedTODO.md` vs `TODO.md` table in the companion
  list; Step B now reads `RefinedTODO.md` and checkpoints into both; **new Step D**
  — the wrap-up/condensing procedure, with first-use bootstrap, keep/drop/delete
  rules, and the two rules that keep condensing honest; Step C recovery now reads
  `RefinedTODO.md`). New companion `RefinedTODO.md`, bootstrapped from the existing
  83-entry `TODO.md`.
- Why: user request — "每次做完一次任务，做一次总结…把之前有悖的逻辑删掉…目的是为了加快
  开发进程，且保证不失真", plus two clarifications during the same turn: `TODO.md` is
  still written every time as the development record ("todo.md作为开发文档需要每次都要
  更新"), and the other companions are updated "如果有需要的话" rather than
  unconditionally.
- Notes on the design, because the risk here is not obvious:
  - `TODO.md` had reached 83 entries / ~220 KB. Reading it to plan a task cost more
    context than the task. But **deleting history to save context is how a project
    forgets why it made its decisions** — so the two files split by JOB rather than
    one replacing the other: `TODO.md` keeps everything and is written but not read;
    `RefinedTODO.md` is condensed and is the one read. Retaining the unabridged
    original is what makes aggressive condensing safe — an over-zealous summary can
    always be checked against it.
  - The condensing rules are written around **why** surviving and process telemetry
    (assertion counts, build results, timings) being dropped: the evidence mattered
    when it was produced, the reasoning matters forever.
  - Contradictions are **deleted, not annotated** — a reversed instruction left
    readable as if current is exactly the distortion this is meant to prevent. The
    one exception is when re-attempting an abandoned approach is a live risk, where
    a single line is cheaper than someone rediscovering the failure. The bootstrap
    applies this to the reverted right-anchored menu.
  - Two safeguards make it honest: durable lessons are **moved to `context.md`**
    rather than summarised away (which is what lets the file shrink while project
    knowledge grows), and an entry that cannot be condensed faithfully becomes a
    **pointer to `TODO.md`, never an invented summary**. The bootstrap follows its
    own rule: entries from this session are condensed from actual knowledge, the
    other ~60 are indexed by title only.

### 2026-07-27 — False positives and IDE editor problems must be RESOLVED, not just diagnosed
- Changed: `SKILL.md` §9 (replaced the "only acceptable non-fix is a false positive"
  escape hatch with the opposite rule — a false positive is not an exemption, just a
  different fix; deleted the old "only leave documented false positives" bullet);
  §9a (new bullet: every `Regex` gets a match timeout the first time); §9b Gate 1
  (now passes only when `get_errors` returns **nothing at all** — classifying a
  diagnostic is not clearing it; perform the restart, re-read, escalate to a window
  reload rather than declaring it benign); new **§10a "False positives must be
  RESOLVED, not merely diagnosed"** — a four-rung ladder (re-check the verdict →
  restructure → suppress with a justification naming what the analyzer cannot see →
  never leave visible or delete correct code) plus the §15 carve-out; §10 gained two
  concrete rule fixes (**S6444** regex timeout incl. the static-initializer
  declaration-order trap, and **S1144** on XAML-bound members — suppress, never
  delete); §15 step 3 made mandatory verification rather than optional confirmation;
  §15 "How to recognize" gained a **PROVE it is stale** block — compare the build-time
  `.g.cs` against the design-time `.g.i.cs` in `obj/` by timestamp and declared-field
  count, plus two refinements (one shared old timestamp across every `*.g.i.cs` = the
  whole design-time build is idle; a name the editor calls missing that IS present in
  the stale file = the server is not reading it at all, so only a restart helps) and a
  note that editing the `.csproj` triggers the same reload; §15 "The fix" step 2 now
  records `dotnet.restartServer` as **confirmed twice** and warns that the breakage
  **recurs within a session** after anything that rewrites `obj/` or the `.csproj`
  under a live IDE — re-run the command rather than doubting the diagnosis, and batch
  edits into one build when someone has the project open.
- Why: two user directives in one session — "To keep the code clean. False positive
  errors should be fixed as well. add this to skill" and "Also, IDE editor problems
  should be fixed too". The preceding gate run supplied the concrete material: 11
  Sonar findings included one true XAML-binding false positive (`ShopPickerWindow.
  ShopRow.Name`, bound as `{Binding Name}` — deleting it would have blanked the shop
  name) and one S2325 that LOOKED like the documented WPF false positive but was
  genuine, because a full-build analysis sees the generated `.g.cs` fields that
  SonarLint's single-file pass cannot. That asymmetry is now written into rung 1.

### 2026-07-26 — Persona statement + English-only Markdown; string-table hygiene rules
- Changed: `SKILL.md` — new "Who this skill is" section stating that `wpf-dev` is an
  English-language full-stack WPF developer that may *converse* in Chinese but writes
  English into the repository, explicitly including **Markdown companion files**; it
  lists the only three exceptions (`Languages.xml` values, a verbatim `- Ask:` quote,
  and naming a string-table value being changed) and requires referring to UI text by
  **key** rather than by its Chinese label. §1 reworded to point at that section and
  gained three new rules: punctuation belongs in the translation (never concatenate
  separators around a localized fragment); **one key per meaning** (two keys bound to
  the same computed value is a bug); and **prune orphaned keys**, with a warning to
  check interpolated key patterns before deleting.
- Why: User directive — "wpf-dev is a English full stack wpf application developer,
  although it may communicate in Chinese. All comments should be coding using English
  language, including the markdown updates." The hygiene rules come from the same
  session's finding that `Order.Fields.DepositTax` and `Order.Fields.ServiceTotalTax`
  labelled the identical computed value, alongside ~20 orphaned keys.

### 2026-07-25 — Roslyn/C# Dev Kit false positives + two-gate pre-build sanity check
- Changed: `SKILL.md` §9 (replaced the "run Sonar before build" bullet with a
  two-gate pre-build sanity check covering BOTH IDE/Roslyn diagnostics and
  Sonar); added §9b (the two-gate sanity check steps); retitled/expanded §9a to
  "Write clean the first time (Sonar + IDE)" with guidance to build + restart the
  language server after adding/renaming XAML windows; added new §15 "Roslyn /
  C# Dev Kit language-server false positives (IDE, not Sonar)" — how to recognize
  stale-model `CS0103`/`DocumentCompilerSemantic` errors, why they occur, the
  fix (restart the C# language server; never edit correct code or suppress), and
  how to avoid them proactively.
- Why: User directive — "add to skills for coding while avoiding Roslyn / C# Dev
  Kit language server IDE problems and Sonarqube errors, follow strict rules
  during coding, then do a sanity check before build for both." Derived from the
  session where ~92 CS0103 editor errors were misattributed to SonarLint but were
  actually a stale Roslyn design-time model, fixed by restarting the language
  server.

### 2026-07-25 — SonarQube zero-tolerance covers any warning/alert/error
- Changed: `SKILL.md` §9 + §10 — broadened the zero-tolerance rule to explicitly
  cover ANY SonarQube warning, alert, issue, or error at every severity
  (Blocker → Info) and category (Bug / Vulnerability / Security Hotspot / Code
  Smell), including pre-existing flags on a file you touched; a task closes only
  when changed files are 100% Sonar-clean.
- Why: User directive — "Add zero-tolerance rule for any Sonarqube warnings
  alert or errors."

### 2026-07-25 — SonarQube fixes are zero-tolerance
- Changed: `SKILL.md` §9 (added a zero-tolerance Sonar bullet), §10 (added a
  zero-tolerance header note + new S3267 "loop should be a LINQ call" concrete
  fix).
- Why: User directive — "Sonarqube fix is no tolerance, should be fixed." Every
  genuinely flagged issue on a changed file must be fixed (any severity) and the
  file re-analyzed clean before a task is done; a green build alone no longer
  closes work.

## Log

### 2026-07-24 — Context-size / compaction recovery flow
- Changed: SKILL.md — added §0 "Step C — Resuming near/after a context-size limit
  (compaction)": re-orient from the companion files by reading `Architecture.md`
  first, then `context.md`'s newest "Recent decisions / state" for the last stored
  context, then `TODO.md`'s last checkpoint; and proactively flush context.md /
  TODO.md before context runs out (treat the files, not chat history, as durable
  memory). Also extended the frontmatter description with this compaction flow.
- Why: user asked the skill to define what to do when a chat session is running
  out of context size — go over the Architecture first and check context.md for
  the last stored context.

### 2026-07-24 — Per-portion tax money model + sortable headers + status labels
- Changed: SKILL.md — rewrote §4 (per-section money) around the per-**portion**
  tax model: a single static `CalculateSectionPayment(...)` returning a
  `SectionPayment readonly record struct`, pre-tax clamped deposit, struct-taking
  cleared/residual/received helpers, and the nominal-vs-received pairing/receipt
  guidance; added §13 (sortable `GridViewColumnHeader`s on a **paged**
  GridView/ListView — sort in the VM before paging, `OrderColumnSort` attached
  `SortKey`/`SortGlyph`, bubbled header-click handler, data-driven
  `GetSortSelector`, RelativeSource arrow indicator); added §14 (read-only status
  relabels the open action via one `RefreshToolbarLabels`, and gating the
  "OrderEdit.PickedUp" checkbox on `cleared || IsChecked`); frontmatter USE-WHEN now lists
  column-header sorting.
- Why: this session implemented the deposit/final split with per-portion card
  tax, clickable column-header sorting on the order list, the completed-order
  "Toolbar.ViewOrder" context-menu relabel, and the picked-up-checkbox gating — captured as
  reusable conventions.

### 2026-07-24 — Proactive Sonar-clean coding restrictions
- Changed: SKILL.md — added new subsection §9a "Write Sonar-clean the first time"
  listing four smells to avoid while typing: high cognitive complexity (S3776),
  non-instance members should be static (S2325, incl. the WPF x:Name inline
  exception), redundant boolean literals (S1125, incl. `bool?` GetValueOrDefault),
  and nested ternaries (S3358).
- Why: User asked to add coding restrictions so these specific errors are avoided
  up front rather than only cleaned up after the fact (they are cross-referenced
  to the existing remediation detail in §10).

### 2026-07-24 — Enforce English-only source code & comments
- Changed: SKILL.md §1 — added a top rule that all source-code language
  (identifiers, comments, log messages, companion-doc prose) must stay English
  even when the task adds/edits Chinese or other non-English UI text; the only
  place non-English text belongs is `Languages.xml` `<Text>` values (route every
  user-facing string through a string-table key, never hard-code CJK literals in
  `.cs`/`.xaml`).
- Why: user requested that development languages (including comments) always
  follow English, even when the request is about adding Chinese language content.

### 2026-07-24 — DataGrid context menus, row keyboard & aggregate duplication
- Changed: SKILL.md frontmatter USE-WHEN (added row context menus / keyboard /
  duplicating a record); §8 (receipt gating now includes a deposit/payment-method
  check, expressed once as a `[NotMapped] bool XxxAddedToReceipt` and reused for
  both the receipt and the detail-panel `Border.Visibility` via the built-in
  `BooleanToVisibilityConverter`); added §11 (DataGrid row context menus,
  commands & keyboard — the Style `Setter.Value` `Click` MC6007 gotcha,
  `PreviewMouseRightButtonDown` row-select `EventSetter`, routing menu items to
  existing commands, and a single `KeyDown` Enter/Delete switch with confirm
  owned by the command); added §12 (duplicating an aggregate record — copy scalars
  without `Id`, fresh natural key, deep-copy children, reset closed status to
  Processing which also clears status-derived flags).
- Why: this conversation implemented a 2K/maximized window, a right-click
  Copy/Edit/Delete/Print menu with row-select-on-right-click, a Copy-order
  feature that resets closed orders to Processing, Delete confirmation, and
  Enter/Delete keyboard shortcuts — and surfaced the reusable
  `Setter.Value` Click gotcha plus the receipt/detail section-gating bug.

### 2026-07-23 — Dropdowns default to the first option
- Changed: SKILL.md §5 — added a convention that when adding a `ComboBox` or
  other single-select picker without a specified default, always pre-select the
  first option (SelectedIndex = 0 on new/setup, fall back to first on edit-load)
  instead of leaving it blank.
- Why: user asked to standardize dropdown defaults and apply it to the
  alteration-category dropdown (default Garment Adjustments / Garment Adjustments).

### 2026-07-23 — SonarQube runs before the build
- Changed: SKILL.md §9 (Workflow discipline) and §10 (SonarQube cleanup) —
  reordered the workflow so SonarLint analysis is triggered and cleaned per
  changed file *before* the kill+build step; added a caveat that standalone
  SonarLint can't see XAML-generated fields (S2325 false positive) and to prefer
  inlining / a post-build re-analysis in that case.
- Why: user request — "Sonarqube process should be run before the build."

### 2026-07-23 — Rename to wpf-dev + skill-update decision flow
- Changed: renamed skill folder `wpf-ordering-app` → `wpf-dev`; frontmatter
  `name` → `wpf-dev`; rewrote SKILL.md §0 into a two-step flow (Step A: classify
  skill-update vs project task; Step B: project-task companions + "save TODO only
  if it differs from the last entry"); added this `SkillUpdates.md` tracker;
  updated the skill-name reference in `TODO.md`.
- Why: user asked to rename the skill and add a skill-only update tracker, and to
  classify each request as a skill update or a project task before touching TODO.

### 2026-07-23 — Session continuity companions added
- Changed: added SKILL.md §0 (session continuity & checkpoints); created
  companion `TODO.md`, `context.md`, `Architecture.md`.
- Why: user wanted conversation state preserved and each request checkpointed to
  `TODO.md`.

### 2026-07-23 — SonarQube cleanup conventions
- Changed: corrected SKILL.md §9 (resolve Sonar issues instead of ignoring) and
  added §10 documenting concrete rule fixes (S1125, S3776, S107, S2325, S3604,
  S3358, S3878, S1075, S2077) and false positives.
- Why: workspace-wide SonarLint cleanup produced reusable rules worth capturing.
