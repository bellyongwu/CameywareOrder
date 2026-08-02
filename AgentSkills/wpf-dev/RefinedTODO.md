# RefinedTODO — CameywareOrder

**This is the file to read.** `TODO.md` remains the full development record and is
still written on every task, but it is 83 entries and ~220 KB — reading it to plan
work costs more context than the work itself.

Maintained per `SKILL.md` §0 Step D. In short: every task appends to `TODO.md` in
full **and** lands here condensed, after which nearby entries are merged and
superseded instructions are deleted rather than annotated. Durable engineering
lessons move to `context.md`; this file keeps *what was done and where it stands*.

Condensed 2026-07-28 from 84 entries.

---

## Where the project is now

A WPF (.NET 8) tailoring order system, multi-shop, EF Core + SQLite, in-process
GraphQL, FlowDocument/QuestPDF printing.

- **Shops** — every shop carries its own name, address (per language), phone,
  email, website, tax registration number, currency, **tax-jurisdiction location**,
  payment/tax rules, receipt numbering, measurement terms, branding and product catalogue.
- **Tax** — a function of the shop's LOCATION, from shipped presets in
  `Settings/System/Defaults/tax-jurisdictions.json`. The location gives a standard rate and a pricing
  MODE: tax added at settlement (Canada, the US) or already inside the price (China, Japan, the EU),
  where the rate is the jurisdiction's alone and the per-method matrix is not consulted. The mode is
  frozen onto each order, as its currency is.
- **Auth** — per-shop memberships with roles, activation, shift; `admin` is
  permanent and never named on screen.
- **Languages** — zh-CN, en-US, fr-FR, es-ES, ja-JP. One file per language, discovered
  from `Settings/System/Languages`; adding a language is dropping a file in — proven
  three times, most recently by ja-JP at a cost of the file and its seed data alone.
  Test scope for any add/removal is fixed in `SKILL.md` §1a and is deliberately narrow.
- **Configuration** — shipped config in `Settings/System/**` (read-only, in git);
  per-installation state under `%LOCALAPPDATA%\CameywareOrder` via `UserDataPaths`.
- **Quality gates** — build 0 warnings / 0 errors, Sonar zero findings, and the
  scratchpad harness suite, which must stay green. A harness that reads live user
  data has to **establish** the state it asserts on: several have gone red months
  later over drifted real data and read like regressions when nothing had broken.

## Open

Nothing in flight. Everything through **v5.0.0 is committed and pushed to `main`**.

**The harness suite is GONE from disk (2026-08-01).** It lived in a previous session's scratchpad,
which has been cleaned up; only `batchcheck`, `datecheck` and `langscope` survive. Treat "the suite
is green" as unverifiable until the harnesses are rebuilt — the lessons they encode are in this file
and in `context.md`, but the assertions are not.

**Neither quality GATE has been runnable for two releases (2026-08-01).** No IDE-diagnostics tool and
no SonarLint tool is connected in this session, so Gate 1 and Gate 2 of `SKILL.md` §9b cannot be
performed as written. The in-build `SonarAnalyzer.CSharp` pass is the whole of the Sonar evidence for
v4.3.0 and v5.0.0. Say so rather than reporting the gates as clean.

**Fixed 2026-07-30:** `langcheck`'s "installs every shipped language" pair was FLAPPING — red, then
green for several runs, then red — because it asserted on live shop data that `storecheck` rewrites
(delete/restore moved Montreal Atelier from `#4` to `#14`). It now seeds its own fixture copy. See
`context.md`: a flapping gate devalues every result it has produced.

**Known odd, not a regression:** shop #10 "Shanghai LeeYonge Bespoke" is located `CA-BC`, so it prices
tax-exclusive and dials +1. Reported to the user rather than corrected: a location change moves the tax
treatment too, which is theirs to decide.

**Deferred, deliberately** — revisit only if asked:

- Externalising `MeasurementTerm.cs` seed data to JSON. The ids are `const string`
  referenced by compile-checked code, so JSON would convert compile errors into
  runtime ones, and the per-shop file users already edit is the real config.
- Wiring shop contact details into the receipt *branding header*. They now print in
  the generated letterhead; injecting them into a custom header too would
  double-print for any shop that typed its address there by hand.
- Moving `measurement-terms-<publicId>.json` into a `Shops/` subfolder. Name-keyed,
  so it needs a migration for no user-visible gain.

---

## Recent work (2026-08-02)

### v8.1.0 — advanced-search disclosure, a shared busy indicator, F5, export-all  [DONE]

Follow-ups to v8.0.0, plus the one piece with a life beyond this screen:

- **`BusyTracker` + `Controls/BusyOverlay` + `ThemedProgressBar`** — the reusable "something is
  happening to the data" module the ask asked for explicitly. State and view are separate so a view
  model never references a control; the tracker COUNTS rather than flagging, because operations here
  overlap (a copy ends by reloading) and a bool lets the first to finish clear the indicator while
  the second is still writing.
- The date/keyword row folded behind **Advanced search**, with a dot on the button when it hides an
  active filter — a list quietly narrowed by something off screen is the failure this prevents.
- **Export replaced Refresh** in the records bar and exports EVERY order; **F5** refreshes.
- **Backup & Recovery moved inside Local Database.** Nesting a differently-gated item made the parent
  need `CanUseDataTools || CanManageBackups`, with each child gating itself.

**What this release is actually worth remembering** is in `context.md`: three separate faults that a
0-warning, 0-error build was blind to and only RENDERING found — a Binding in a template Storyboard
(fatal at window load), a StaticResource to the theme from a reusable UserControl (fatal at control
seal), and an animation range that left the bar visibly empty.

### v8.0.0 — the data release: automatic backup, order search + CSV, a 30-day recycle bin  [DONE]

Came out of a completeness review for a LOCAL single-shop deployment (installed on site, no online
database). Three findings, all on the data side:

- **No automatic backup existed at all.** One is taken before a destructive import and nowhere else,
  so a shop that never touches Import/Export has zero. Local SQLite plus no backup is one disk
  failure away from losing the whole trading history.
- **No search by order number and no spreadsheet export.** The list matched customer name and phone
  only, and the only export was a zip of the database — useless to a bookkeeper.
- **No recovery from a delete.** Confirmed, but permanent.

Design rule for the release: ONE definition per concept. The filter is a model shared by the list,
the export and the bin; the backup reuses the existing package format rather than a second copy
routine; the two retentions are one settings store and one panel.

**What it added.** `Order.DeletedOnUtc` + a second condition on the Orders query filter;
`OrderRecycleBin` (the ONE place an order is deleted from — the list, the Delete key and the GraphQL
mutation all route through it); `DataProtectionSettings`/`DataProtectionStore` (per-installation, one
file, one panel for both retentions); `BackupService` (run-if-due at startup before any window opens,
reusing `ExportDatabaseTo`/`ImportDatabaseFrom` unchanged); `OrderQuery` (text + field + status +
period, replacing two view-model fields and three `if`s the export could not have called); `CsvWriter`
+ `OrderCsvExport`; `DataProtectionWindow` + `RecycleBinWindow`; three capabilities.

**The lesson worth carrying forward is the AUDIT** — see `context.md`. A second condition on a query
filter changes every `IgnoreQueryFilters()` caller at once, because dropping the filter drops both
halves. Four callers needed the shop half restated and one, `OrderNumberFormatter.IsTaken`, was a live
defect: a binned order's receipt number read as free.

**Not covered by a harness, deliberately and stated at the call site:** writing and restoring a real
backup. Both copy the machine's real LocalAppData and there is no seam to redirect it.



### v7.1.0 — creating shops moves to Store Management; demo data; copy shop; modular copy/paste  [DONE]

1. **Create Shop and Create Demo Store left the shop picker** for a new *Add a store* card in Store
   Management. The picker chooses; Store Management decides which shops exist. A shop created there
   is reported back as `CreatedShop` and SELECTED in the picker rather than closing it — the old
   Create button closed the picker, which is the wrong guess for an administrator who went in to make
   two branches. `ConfigureTermsRequested` now travels Setup → Management → Picker → whoever OPENS
   the shop; none of the three can act on it, because MeasurementTermsService edits the BOUND shop.
2. **A demo store arrives with 100 preset orders** from `Settings/System/Defaults/demo-orders.json`.
   `Shop.IsDemo` limits it to one per installation; deleting it brings the button back. See
   `context.md` for the three rules the data file encodes (offsets not dates, same-day records, a
   demonstration tax rate) and for why the seeder swaps `PaymentTaxRules.Active`.
3. **Copy Shop** duplicates configuration and the three per-shop files, never orders. The suffix is a
   string-table value (`Store.Copy.Suffix` / `.SuffixNumbered`) because it is punctuation as much as a
   word — zh writes `（复制）`. The number is chosen once per shop and applied to EVERY language, or one
   shop tells two stories about which copy it is; and the batch adds its own names to the taken set as
   it goes, the same defect batch Copy Order shipped with once.
4. **`Controls/CopyPasteBinding` + `ICopyPasteSurface` + `Services/AppClipboard`.** Five members and a
   declaration in the markup is the whole of what a screen supplies. Attached to the LIST, not the
   window, so Ctrl+C in a search box still means "copy this text". Bound today to the orders list and
   Store Management; `surfacecheck` asserts the pairing against the SOURCE so the third such list
   cannot quietly grow its own `KeyDown` switch.

### v7.0.1 — hotfix after the permissions release  [DONE]

The one real defect: **the main window's month summary obeyed the permission and then stopped
obeying it.** `ApplyRolePermissions` hid the strip; `RefreshSummaryStrip` showed it again whenever
the month had figures, and that runs on every order reload. The capability check now lives inside
`RefreshSummaryStrip`, which is the strip's ONE owner. This is the recorded one-owner rule, broken
five hours after writing the release that depends on it — the harness now asserts both that the
owner checks and that nothing else writes the property.

The settlement report gained a **period picker**: one `Calendar` in a popup, `DisplayMode` chosen
from the chip. Day is an ordinary selection; **Month and Year are read from `DisplayModeChanged`**,
because a Calendar in Year or Decade mode drills down instead of selecting — the drill-down is the
choice, and `e.OldMode` is what stops opening the popup from reading as one. ESC closes the report,
and an open picker takes ESC first.

That surfaced a **latent theme bug**: `CalendarButton` (the month/year cells) had an implicit style
that had never once applied. `Calendar` hands its cells whatever `CalendarButtonStyle` holds — the
same trap already documented for `CalendarDayButtonStyle` — so the drill-up views had always been
stock Aero. Now keyed, set on `ThemedCalendar`, and templated.

Shop picker: Permissions before User Management, and four `Accent*Button` styles (indigo / teal /
amber / green). Border and text alone were invisible at button size; each carries a soft fill too.

Login and lock screens take `ResizeMode="CanMinimize"`. Login needed `ShowInTaskbar="True"` with it —
minimising a window that has no taskbar button leaves nothing to click to get it back, which is worse
than the missing button. The two are asserted as a pair.

### v7.0.0 — permissions became data  [DONE]

The permission model was three fixed `UserRole` values and named properties comparing against them
(`IsAdministrator || CurrentRole == Manager`). That is a set of answers baked into the build: an
installation wanting a role that reads the settlement report and touches nothing else could not say
so. Now a role is a **named set of `AppCapability` values** in `roles.json`, and every gate asks
`AuthenticationService.Can(...)`.

**The shape.** `CapabilityCatalog` is 19 capabilities in 5 groups, each carrying a `Scope` and an
`AdministratorOnly` flag. `RolePermissionStore` owns `roles.json`; `ShopMembership.RoleIds` (schema 5)
names roles by id. `UserRole` survives ONLY to read older files — `LegacyRoleIds.For` maps it.

**Decisions, all of which were asked and answered:**
- ONE installation-wide catalog; users are assigned roles per shop. Per-shop role *definitions* were
  rejected: "Auditor" would mean two things in two branches and the word would stop being readable.
- Built-in Manager/Staff are editable but not deletable. The **administrator is never persisted** —
  it is regenerated from `BuiltInRoles.Administrator()` on every load, because it is defined as
  "every capability there is" and a stored copy would be frozen as of the release that wrote it.
- No per-user capability overrides. Roles are the only source, so "why can he do that" has one
  answer.
- Auditor is **seeded once** (like the seed accounts) rather than built in, so deleting it sticks.

**Scope is not cosmetic.** A shop-scoped capability resolves against the shop currently open; an
installation-scoped one against ANY active membership — the shop picker asks "may you create a shop"
with no shop bound, so a shop-scoped answer there could only ever be "no". `ManageUsers`,
`DeleteAccounts` and `ManagePermissions` are administrator-only and stripped on the way in AND on the
way out: a role that could grant capabilities could grant itself everything.

**Fails closed.** A membership naming a deleted role grants nothing, and `CanAccessShop` requires at
least one role that still RESOLVES — otherwise the shop opens to a window with no records, no buttons
and no explanation. Deleting a role withdraws it from every holder in the same operation
(`RolePermissionStore.Delete` → `AuthenticationService.DropRole`), but leaves the membership in place
with no roles: visible on the roster, and fixable.

**The upgrade takes nothing away.** Manager and Staff ship with exactly what the hard-coded rules
granted, *including* Staff keeping the settlement report — the Settlement menu was never gated, and a
permissions release that starts by silently removing a screen from every shop assistant is a
regression wearing a feature's clothes.

**The two existing role screens had to become catalog-driven**, and this was not optional: they wrote
back a hard-coded Manager/Staff pair, so saving a member from either would have STRIPPED any other
role they held. `Controls/RoleCheckList` and `RoleToggle` are the shared picker.

`PermissionsWindow` (Local Configuration → Permissions, and the shop picker) is two trees: accounts →
shops → role tick boxes on the left, shops → roles → capability tick boxes on the right. **One
`RoleNode` instance is shared by every shop**, because a copy per shop would show the same role in
two contradictory states and only one could be saved. Everything writes on Save, never per tick — a
panel that saved as it went could revoke the administrator's own access mid-edit.

### v6.0.0 — settlement reporting  [DONE]

Local Configuration → Settlement Report. Opens on **this month**; Day / Year / custom range and
previous-next stepping. Shows before-tax, tax, after-tax, received and outstanding — split by service
line (alterations / made-to-measure / ready-made) and by payment method (cash / card / transfer) —
plus order counts including unfinished, cancelled and returned. A doughnut and a bar chart, a PDF on
the shop's own letterhead, and a summary strip on the main screen.

**The reuse analysis was the task's first requirement, and it shaped everything.** Nothing about
money is recomputed: every figure comes from `Order.MoneyFor(line)` → `SectionPayment`, which already
holds both pricing modes, the per-portion tax rules and the deposit clamp. `Order` gained
`MoneyFor` / `ReceivedFor` / `OutstandingFor` / `MethodFor` / `SplitFor` so a consumer selects by
`ServiceLine` rather than copying per-section rules — the report was the SECOND consumer, and a copy
would have been free to disagree with the receipt the customer is holding.

Left behind for the next feature: **`DateRange`** (a calendar period with nothing about money in it),
**`SettlementCalculator`** (pure — window, PDF and main screen read one set of numbers), and
**`Controls/Charts`** (`BarChart`, `PieChart`, palette, and `ChartImage`, which puts the SAME element
into the PDF rather than redrawing it).

Two rules worth keeping:

- **The stage total is authoritative; a payment split only says how to divide it.** Split lines are
  pre-tax, so summing them leaves the method figures short of the money received. The known stage
  total is apportioned by share instead, which keeps cash + card + transfer = received.
- **Refunded orders are counted but earn nothing**, and their value is reported on its own line
  rather than dropped — hiding it would be as wrong as counting it.

*Found by rendering:* the preview picker's hard-coded grey label was invisible on a dark header. It
now takes the host's `Foreground`.

**Not done:** no assertion harness for the calculator. The one invariant that matters (methods sum to
received) is checked by the demo seeder's reporter, not by a test. That is the first thing to add.

### v5.1.0 — the expected pickup date, and a list built around it  [DONE]

Every order now records the day the customer is coming back. Required, blank by default, and refused
unless it is in the future. It sits on the same row as the order date.

The list is a **work queue** now: ordered by pickup day soonest-first, rows tinted amber inside two
weeks and red once the day has gone. `Order.PickupDue` is derived from `DateTime.Today`, so nothing
persists "overdue" and nothing can go stale.

Two things rendering caught that every assertion had missed:

- The **selection highlight painted over the tint**, on the exact row the ordering puts first — the
  most overdue one. The tint now draws above the highlight and stays translucent.
- **Finished orders sat at the top**, because a job collected last month carries last month's pickup
  date. `IsPickedUp || IsRefunded` is now the first sort key. A header click still sorts by the date
  alone.

Orders predating the field have no date and none was invented: they sink below the dated ones and are
never coloured.

### v5.0.1 — the build knows its own version  [DONE]

`Directory.Build.props` carries Version / AssemblyVersion / FileVersion / InformationalVersion.
Before it, a shipped exe reported 1.0.0 for every release ever made. `AssemblyVersion` moves only on
a major; `InformationalVersion` is the only one allowed a suffix.

### Demo data  [DONE]

`scratchpad/demoseed` builds shop **#5 "Demo — Pickup Dates"** with 50 orders spread across all three
colour states plus finished ones. It only ever ADDS — verified as exactly +1 shop and +50 orders with
nothing else moved, and the real database was backed up to the scratchpad first. Re-runnable: the RNG
is seeded, so the same demo comes out twice.

### v5.0.0 — a panel can be read in a language the application is not in  [DONE]

Checking a translation meant switching the whole app into a language, finding the screen, reading it,
and switching back. Measurement Terms now has a **Preview in** picker: the panel — headings, buttons,
and every garment and term NAME — re-reads in the chosen language while the application stays put.

Built as two reusable pieces, which is why it is a major version: `LocalizationScope` (one panel's
own language; declare it in `Resources` and every existing binding changes by one word) and
`LanguageScopeSelector` (the picker; drop it on, point it at the scope, done). Any panel can have
this now.

The rule that shapes it: **what is being EXAMINED follows the preview; what you must ACT on does
not.** The picker's own label, the confirmation dialogs and the warnings stay in the reader's
language — preview Japanese with a Japanese picker and there is nothing left to click to get back.
An inline rename deliberately writes into the PREVIEWED language, which is what makes the screen
useful for filling translation gaps.

Three traps are in `context.md`: a `Window`'s Title cannot bind to a scope in its own `Resources`
(properties are set before Resources exist); code-built rows need rebuilding from `TextChanged`
because a binding refresh cannot reach them; and the scope must be `Detach()`ed or the singleton
holds the window forever.

*Found by rendering:* the picker did not follow a scope moved by its HOST, so it named a language
that was not on screen. Now asserted in both directions.

### Source tree split three ways  [DONE]

`Views/`, `Models/` and `Services/` each hold `UserManagement/`, `StoreManagement/` and `Global/`.
Orders live under StoreManagement — a shop's daily work, not chrome.

**Namespaces deliberately unchanged.** What made the move a pure `git mv` was checking first that
nothing references a source PATH; the two `Themes/` dictionaries do, by absolute pack URI, and did
not move. Delete `obj/` afterwards or the old generated partials compile alongside the new ones.

---

## Recent work (2026-07-31)

### v4.3.0 — the order date is a field, and it can be backdated  [DONE]

An order taken on Monday and typed up on Wednesday was stamped Wednesday, because `OrderDate` was
only ever `DateTime.UtcNow` at save. Now a picker in the Basic Info card, in the right-hand input
column, refusing any day after today.

It records a **date**, not an instant, and writes only when the picked day differs from the one
already recorded — so an untouched picker keeps the live timestamp and an untouched edit still reads
as "no change" to the EF-driven modification check. Backdated days are stored as LOCAL midnight in
UTC; `SpecifyKind(day, Utc)` reads back a day early everywhere east of Greenwich.

*Found on the way in:* the list and the detail panel bound `OrderDate` **raw**, so a shop in +08 has
been seeing morning orders under the previous day since the beginning. `Order.OrderDateLocal` is now
what every surface binds, and the receipt prints the day without a time — the record is a date.

Rules for the future: **blackouts, not `DisplayDateEnd`** (which hides later days entirely and does
not refuse a typed one); **`CalendarSizing` floors the width rather than fixing it** (the month grid
is content-sized, so a hard width clips columns); and a **harness must not drive a refused Save** —
the dialog blocks the thread. All three are in `context.md` with the measurements behind them.

Two pre-existing quirks the column-diff turned up, reported and not fixed: an order whose service
category is "None" clears its section on the next save, and the legacy aggregate
`Orders.FinalBalanceMethod` reconciles itself on the first re-save.

### v4.2.0 — select several records and act on them at once  [DONE]

Ctrl+click picks records on the orders list, Ctrl+A takes the current page, and Copy and Delete act
on the selection — those two only.

`SelectionMode="Extended"` (never `Multiple`, which would make every plain click a toggle). The
selection lives on `MainViewModel`, pushed in by the view because `SelectedItems` cannot be bound;
`SelectedOrder` stays the anchor. Everything that is not Copy or Delete — Edit/View, the three Print
entries, Enter, double-click — is gated on **exactly one** row.

The durable lessons moved to `context.md`: batch inherits the single action's latent defects; a
rebuild must collapse the selection; right-click replaces rather than extends; Ctrl+A is
`ListBox.OnKeyDown` and not a command; render only after the animation settles; a harness must load
the string table itself.

Copy, which the batch is built on, had two problems:

- **Fixed.** It hand-composed `ORD-{timestamp}` instead of going through `OrderNumberFormatter`, so
  it ignored the shop's own prefix and mode — and a batch copied inside one second gave every copy
  the SAME number. `Reserve` could not save it as written: it returned early in Timestamp mode,
  ahead of the collision scan its own summary promises. Both fixed; proven by reverting the fix and
  watching `batchcheck` go red on three copies sharing `ORD-20260731-194353`.
- **Still open — reported, the user's call.** The copy's property list has fallen behind the model.
  It drops `PricesIncludeTax`, `PaymentSplitsJson`, the three `*FinalTaxRate` columns,
  `StatusReason`, `StatusReasonCategory` and `LastModifiedBy`. The money ones mean a copy in a
  tax-inclusive market comes back priced under the other arithmetic — a pre-existing defect of Copy,
  not of the batch, but the batch repeats it once per record.

### v4.1.1 / v4.1.2 — phone numbers are validated properly, everywhere

Reported: `289-990-33577` saved on a Canadian order. It was two defects and then a third.

**The strict rule reached only NEW orders** (`_existing is null ? IsValid : IsValidLoose`). The
leniency is right — an order taken last year must stay saveable without re-typing a number nobody can
verify — but keying it to the ORDER meant an existing one accepted any 7-to-15-digit number in any
country, at every wrong length from 6 to 13. **Leniency belongs to the VALUE**: the field remembers
what `Load` gave it, and only an untouched stored number keeps the loose rule.

**Validation counted digits**, which cannot see an area code starting 0 or 1, a Chinese mobile not
starting with 1, or a French number carrying a trunk zero — nine such numbers were accepted, each the
right length. `nationalPattern` in `phone-countries.json` now decides, matched against digits alone
with the count as fallback. Patterns are asserted anchored (unanchored matches a substring and
validates nothing) and each country has a positive case (a pattern refusing everything passes every
negative test). **Japan ships no pattern on purpose** — it writes `090-1234-5678`, `90-1234-5678` and
`03-1234-5678`, so any leading-digit rule refuses one real form; the first attempt did, and an
existing assertion caught it.

**Sharing the control had not shared the rule.** `CustomMadeServiceWindow` hosts the same field and
validated neither phone nor email — one implementation and one omission. The decision moved onto the
control as `IsAcceptable`, and the harness asserts the two windows AGREE on the same inputs rather
than testing each separately. Both lessons in `context.md`. `phonecheck` 167 → 221.

## Recent work (2026-07-30)

### v4.1.0 — a save that changed nothing, and locking the session

**Change detection.** Opening a record, pressing Save and changing nothing no longer stamps
`LastModifiedDate`/`LastModifiedBy` — which had been overwriting who last EDITED an order with who
last looked at it. Answered by asking EF whether the tracked entity is modified rather than hashing
the form: EF compares against the database column by column, covers the JSON blobs the form does not
model as fields, and does not drift as columns are added. Two things had to change for it to work at
all — the stamp moved out of the apply-the-form method (an unconditional `UtcNow` makes every save a
change), and the clothing items are compared by value instead of being removed and re-added every time.

A record can be genuinely changed by merely OPENING it: an order stored before some field existed
comes back with nulls the form cannot represent, so the editor's defaults are written on the first
save and it is correctly stamped once. The harness names the columns a no-op save moved, which is what
distinguished that from broken detection. Both in `context.md`.

**Lock the session.** ESC on the main window, or the toolbar's Lock button, opens a themed panel
offering Lock or Sign out. A lock keeps the user AND the shop, asks only for the password, and reopens
the same store with no picker. It holds no credential while locked — `SignOut()` really is called, so
every capability gate answers no — and what makes it a lock rather than a sign-out is only that the
shop is remembered. Both remembered values live in locals for the length of one method, so nothing
about a locked session survives the process.

There is no Cancel: closing the window signs out. Only the locking account can unlock. Access is
re-checked through the same accessible-shops filter the picker uses, so a membership revoked while the
machine sat locked sends the user to sign-in rather than back into the shop.

New `lockcheck` harness (25). Both windows were rendered — and rendering caught the horizontal
`StackPanel` clipping its own description, the exact trap this file documents from v4.0.2.

**Fixed on report:** the lock screen showed `DisplayLabel` under a field labelled `Login.UserName`, so
it named the person while authenticating the login. The harness could not have caught it — its test
account had no first or last name, so `DisplayLabel` falls back to the user name and both readings
agree. A fixture sitting in the fallback case cannot tell apart the branches it is meant to choose
between; the regression test now uses a named account and was confirmed to fail before the fix.

## Earlier work (2026-07-30)

### v4.0.4 — a tax rate with three decimals

Quebec's combined GST+QST is 14.975%, and the application could store it but not keep it. Rates were
`decimal` end to end and persisted perfectly; every DISPLAY used `"0.##"`, and the settings screen seeds
its rate box from that formatted string — so opening the tax settings for any reason and pressing Save
rewrote 14.975 as 14.98. Six cents of tax on a $600 sale, silently, on every save.

`Models/TaxRateFormat.cs` is now the one definition: the three-decimal limit, the partial-input pattern,
`TryParse` with the 0..100 range, and the display text. Nine `"0.##"` sites call it. Three different
answers to "what is a rate" had been live at once — the box accepted any text, the parser demanded
0..100, the display rounded — which is what made the drift invisible.

The lesson generalises and is in `context.md`: **a format that an edit box is seeded from is part of the
data path, not decoration.** Its regression test drives the screen and saves twice; asserting the stored
decimal alone passes throughout, because storage was never what broke.

**Money now rounds**, which it never did — `Models/MoneyRounding.cs`, two places, half away from zero, so
89.425 is 89.43 rather than banker's 89.42. The absence was invisible because `ToString("N2")` rounds on
the way to the screen: every figure LOOKED right while the values behind them kept full precision, and
only a total printed beside its own parts could show the disagreement. Split lines round per line before
summing, because each line's tax is printed beside its amount. The third decimal on rates is what made
this urgent — 14.975% lands on a half-cent constantly.

**Sonar runs in the build now** (`Directory.Build.props`). Run as an analyzer for the first time rather
than read out of the IDE's Problems view, it found 9 issues across 6 files in a workspace that had been
called clean repeatedly — `Any`→`Exists`, `FirstOrDefault`→`Find`, two prose comments whose trailing
semicolons parsed as code, and two byte-identical event handlers. All fixed; the baseline is zero.

### v4.0.3 — a phone number punctuates itself, and the reason section wraps

`phone-countries.json` gained `nationalFormat`, keyed by DIGIT COUNT rather than by country, because a
country can write two lengths differently. Canada and the US group `###-###-####`, China `### #### ####`,
France `# ## ## ## ##`, Spain `### ### ###`, Japan's 11-digit mobiles `###-####-####`. **Japan's
10-digit numbers ship no rule on purpose** — 03-1234-5678 and 045-123-4567 are both correct and the
digits do not say which — so they are left exactly as typed, which is also what any country the file
says nothing about gets.

`PhoneCountry.FormatNational` is progressive, so it runs on every keystroke: a separator is emitted only
when a digit still follows it, so a half-typed number never carries a dash it has not earned. The caret
comes from `TextChangedEventArgs.Changes`, not `SelectionStart`, which differs by how the text arrived
and cannot be tested in-process. Backspace onto a separator takes the digit in front of it. A stored
number is re-punctuated only when there is an exact pattern for its length — an extension, a note or a
number that never fitted the country comes back untouched.

The cancel/return reason label was clipping because `OrderEditWindow` declares its **own** keyed
`FieldLabel` with no `BasedOn`, so the wrapping added to the theme in v4.0.2 never reached it. Third
time that shape has cost a session; the rule is now in `context.md`. The reason picker and its
placeholder wrap too.

The main list's custom-service column stacks the flag over the garments — `Yes` / `(Qipao, Shirt)` —
with the second line a fixed height that stands whether the names show or not, so rows stay level; both
lines fit inside the row's existing 54px minimum, so no row grew.

### v4.0.2 — the selection controls are drawn, and two labels that would not wrap

`ThemedRadioButton` and `ThemedCheckBox` replace the stock Windows controls in `AppTheme.xaml`, each
keyed **and** implicit, matching `ThemedTextBox`. Both answer the pointer with a halo drawn OUTSIDE the
ring/box, because a fill that appears on hover reads as half-selected. The checkbox keeps its fill when
checked **and** disabled: a locked "deposit received" must still read as received, and the stock
grey-out says the opposite of what is true.

Keyed variants had to be hunted down and rebased or they would have kept the stock look while
everything around them changed — `MethodRadio` (42 radios) and `ShopSetupWindow.ModeRadioStyle` now say
`BasedOn`; `FilterChip`, `ChallengeBox` and `NotApplicableCheckBox` carry their own templates and were
left alone. `CustomMadeServiceWindow` declared a local implicit `<Style TargetType="TextBox">` with no
`BasedOn`, which REPLACES the theme's rather than extending it — that one line is why every input in
that window looked wrong; fixed, with three inline paddings and 16 hex literals mapped to the palette.

Two "label overflows" reports had one cause and it was not the labels: **a horizontal `StackPanel`
measures its children at infinite width, so `TextWrapping` inside one is inert.** The 8 basic-info
label blocks became `DockPanel` + docked icon + `VerticalAlignment="Center"`, which satisfies both
halves of that ask at once. The breakdown labels were the other case — wrapping was live, the column
was just too narrow: `120`→`158`. Both compiled, ran and asserted green the whole time they were wrong,
so both were verified by rendering. `context.md` carries the rule.

### v4.0.1 — the split allocates itself
Three bugs and five behaviours on top of v4.0. The bugs shared a shape: **"Skip deposit" left the split
rows populated** (so a stage told to take nothing still owed something and could never balance), and the
**final-balance breakdown was keyed to the deposit-received tick**, which a skipped deposit never sets —
so those orders reached their balance with nothing explaining it. The toggle is now offered at the
balance stage too, mirrored onto the deposit pair that remains the one the flag is read from.

The allocation now helps: every unanswered row OFFERS the remainder as a placeholder, clicking into one
commits it and settles the others at zero, and what it writes is ordinary editable text. Blank and 0 are
deliberately different states — a typed 0 is an answer and is never overwritten. "Deposit received" is
refused until the rows add up, over-allocation names the CEILING rather than the overshoot, and each row
states its own tax and receivable (`13% tax $39.00 · due $339.00`).

**The gate went in the wrong place first and did nothing**: `ApplySectionLock` owns
`DownCompletedCheck.IsEnabled` and assigns it unconditionally, so a rule applied during the refresh pass
was overwritten milliseconds later. A control's enabled state has one owner (`context.md`). Caught only
because `splitcheck` grew an EDITOR section — placeholders, focus, editability and enablement are
control state, invisible to a model-only harness.

### v4.0 — one stage, several payment types
A 600 deposit paid 400 cash + 200 card is now recorded as that, and taxed as that: **26.00, not 78.00**.
The old model held one method and one rate per portion, so it could only record one of the two — which
is not a display limitation, it is the wrong tax.

`Order.PortionTax` takes an optional line list; no lines is the rule the app always had. The input moved
into `SectionPaymentInput`, a struct, so the compiler enumerates every call site when the next field is
added — the pricing-mode flag taught that lesson by shipping optional and letting a harness keep the old
arithmetic silently. Storage is ONE nullable column (`Orders.PaymentSplitsJson`), empty for the whole
installed base, so nothing is recalculated on upgrade.

Decisions taken with the user first: fixed rows one per method (no add/remove list state) with a live
"left to allocate"; the toggle per SECTION covering both its stages; the typed deposit stays the target
the lines must meet, which is also the only shape the final stage can have; and a stage that does not
balance is REFUSED, because a shortfall is a partial payment and no such state exists anywhere in the
model, the receipt or the balance column. Offered only where tax is added at settlement — where the
price contains it, the tender cannot move it.

### A phone number carries the country it belongs to
Every phone field is now one control — `PhoneNumberField` — with a dial-code picker and a drawn flag in
front of the number, on all five surfaces that collect one. The country is per NUMBER, not per shop: a
Toronto shop takes a visiting customer's Shanghai mobile, and a rule keyed on the store would refuse a
correct number. The shop's LOCATION only decides what the field opens on, with its currency as the
fallback for a shop that never said where it is — location first because the currency table maps EUR to
France, so a Barcelona shop defaulting off its currency would offer +33 for every number it takes.

**Storage did not change**: one string column, `"+86 138 0013 8000"`, read back by longest-dial-code
match. A legacy number naming no country comes back WHOLE under the shop's country rather than being
reformatted — it is a fact about a customer, not something to tidy. And the strict national-length rule
binds **new records only**; an existing order keeps the loose 7–15 rule, because an order taken last
year must stay saveable over a phone number nobody can re-verify.

Three things worth not rediscovering, all in `context.md`: **Windows ships no flag emoji** (a
regional-indicator pair renders as two letters, so the six flags are drawn as vectors); **a relative
`ResourceDictionary` Source resolves against the loading APPLICATION**, which killed every harness the
moment `AppTheme.xaml` nested a flags dictionary; and **replacing a TextBox breaks the abstractions
built on TextBox** — `RequiredTextField` now holds closures so the phone stays inside the one-pass
required check.

Demo data reseeded per store: 193 rows, backed up first, generated through `PhoneCountries.ForShop` so
what is seeded cannot disagree with what the screen shows. A shop's own number is KEPT when it already
belongs to that shop's country — only orders are mock data.

The suite paid for itself twice here. Four harnesses went red on the control rename — the good failure,
and only because they were run. The fourth was NOT a rename: `shopcheck` rendered `+1 +86 20 1234 5678`
because `SystemSettingsPaths` probed for the *folder* `Settings/System` rather than for the file it
needed, so an output directory holding a partial copy won the probe and every missing file degraded
silently to a built-in fallback. That is fixed in the app, not worked around in the harness.

### A tax-inclusive order explains itself in its own words
The panel was written for tax added at settlement and still read that way where the tax is already in
the price: `Order.Fields.PreTaxServiceTotal` over a price that is not pre-tax, a rate box switching
between `Order.Fields.DepositTaxRate` and `Order.Fields.FinalTaxRate` when the two cannot differ, a
deposit-stage breakdown whose every line is the price restated, and a final stage carrying twelve rows
of exclusive arithmetic.

Four gaps in the ask, settled with the user first — the two that matter: **the tax has a name and it is
not the same name everywhere** (a VAT in China and the EU, a consumption tax in Japan), so a
jurisdiction now DECLARES its tax name (`taxNameLabel` → `TaxName.*`) the way it already declares its
tax number; and **the four requested rows say nothing about a final balance once it is paid**, so
`Order.Fields.ReceivedFinalBalance` appears as a fifth row when non-zero. Receipt and detail panel take
the same wording — a customer-facing document disagreeing with the screen is the version that gets
questioned.

Two shapes worth keeping: the deposit-stage breakdown is **deleted** in that mode rather than reworded
(all four of its lines restate the price), and the inclusive final stage is a **sibling panel** rather
than the same grid with rows hidden, because the rows that survive want a different order and
`Grid.Row` is fixed in markup. Both panels are written in one pass from one `SectionPayment`, which is
the only thing keeping two views of one order in step. Reasoning in `context.md`.

`taxcheck` grew a section that drives the ORDER WINDOW in both modes — the defect was never
arithmetic, it was vocabulary printed over correct arithmetic, and only a rendered panel shows that.

### Canada — sales tax added separately, and ONE entry rather than three
The three provincial rows (ON 13 / AB 5 / BC 12) became one `CA` at rate 0: Canada is treated exactly
as the US already was, seeded **tax free** with the shop entering the rate it collects, and the picker
says "added separately" in all five languages. Once no province quotes a rate, three rows differ in
nothing but their name.

**The region machinery stayed** — that was the explicit ask. Codes are free-form (`<country>-<region>`),
the `TaxJurisdiction.CA-*` label keys stay in every language file marked dormant, and re-adding a
province is a line of JSON. `TaxJurisdictions.For` now widens an unshipped regional code to its COUNTRY
(`CA-ON` → `CA`, `US-CA` → `US`) before the home-market fallback: without it, every shop already stored
under a provincial code reached the right answer only *by luck* — the home market happens to be Canada —
and would have started reading wrong the day that changed. `Find` stays strict, because the settings
screen relies on its null to tell a live code from a dead one, and no migration rewrites a stored code,
so a re-added province takes effect on its own.

### Store Management — delist, delete, download, restore, reinitialize
Administrator-only panel off an enlarged Select Shop (820×640 → 1000×740), with ctrl/shift multi-select.
`ShopAdministration` owns the rules, `ShopArchive` the file format, `ConfirmDestructiveWindow` the gate: a
10-character phrase generated per dialog, typed exactly, before either of its two buttons (save the records
first / remove now) enables. Restore is from a user-picked file — the user chose that over an in-app
archive — and reinitialize keeps accounts, so nobody can lock themselves out. Plus a one-click demo store
built from the shipped presets.

Four findings, each a wrong first move worth not repeating:

- **`Shop.IsArchived` already existed and already meant "delisted"**, honoured in three places, with no UI
  to set it. I designed a parallel flag before grepping for prior art. The bool stays authoritative;
  `IsDelisted` delegates; the new timestamp is an audit stamp, not a second opinion.
- **`StampNewOrdersWithShop` would have re-parented every restored order** — it overwrites `ShopId`,
  currency and pricing mode from the OPEN shop, which is right everywhere except an importer that already
  knows all three. Hence `SuppressShopStamping()`, one explicit scope.
- **Deletion reaches outside the database**: per-shop files are named after `PublicId`, so one outliving
  its shop later hands a NEW shop an old one's branding.
- **It shipped with no harness and the first export threw** a JSON object-cycle: EF's fix-up populates
  `OrderItem.Order`, and nothing had ever serialized an order. `[JsonIgnore]` at the source, and
  `storecheck` (50 assertions) now covers the round trip, additive restore, bad input, delist, deletion
  isolation and the challenge generator.

The delete phrase was also uncopyable and low-contrast — `ThemedTextBox`'s `IsReadOnly` trigger repaints
the chrome through `TargetName`, which beats a local `Background`. All four lessons are in `context.md`.

### One path for a refused save: banner + inline message + one dialog
`OrderEditWindow` had eleven validation checks and no rule behind how they reported — five raised a
dialog, two wrote a message under their field, and every one set an `ErrorText` sitting at the FOOT of a
form taller than the window. The customer name got none of the three.

Three surfaces now, each answering a different question, all from `Fail(key, inline, focus)` /
`TryRequireFilled(fields)`: a dialog (something is wrong NOW), a banner above the form and outside the
`ScrollViewer` (what), a red line under each input (where). Required-empty fields are collected in ONE
pass so two missing fields are reported as two — fail-fast could only ever name the first. Messages
clear at the start of each pass, as the user types, and when the control they belong to is hidden.

`TryValidateForSave` owns the dialog and delegates marking to `ValidateForSave`. That seam is load
bearing, not tidiness: a `MessageBox` inside a check blocks the thread, so the harness would hang on it
— the same trap as the reseed confirmation. New `validcheck` harness (41 assertions) drives the marking
half. Reasoning in `context.md`; the general rules are now `SKILL.md` §4b.

### The currency picker offered a currency with no tick box
`ShopLocalizationWindow` seeded one row per currency the **system's** languages offer, plus whatever
the shop already accepted, while the cards on the right are grouped by the languages the **shop** runs
in. Nothing reconciled the two. A real shop stored `["CAD","JPY"]` against `["en-US","fr-FR"]`, so JPY
had a ticked row in no card at all — invisible, listed in the preferred-currency picker, written back
on save, and impossible to remove.

`TickedCurrencies()` is now scoped to what the ticked languages bring, which is exactly what the cards
show, so **the panel returns exactly what it shows**. That replaced a deliberate rule that kept such a
currency so a branch would not "silently stop taking money it had said it takes" — right intent, wrong
mechanism: an invisible tick preserves a value by making it unmanageable. The floor is guarded instead,
live: `EnsureOneCurrency` shows the red inline line and re-ticks the first offered currency on every
toggle *and* on the way in, rather than refusing at Done. Full reasoning, and the three other lessons
this turn produced, in `context.md`.

Diagnosis came from reading `SupportedCurrenciesJson` against `InstalledLanguagesJson` in the live
database. Every fixture had ticked the language that brought the currency, so the two sets coincided
and the defect could not appear — the regression test now uses the reported shop's stored state.

## Recent work (2026-07-29)

### Store location decides the tax, and whether prices already contain it
`Shop.LocationCode` names a jurisdiction from a shipped preset file
(`Settings/System/Defaults/tax-jurisdictions.json`); the jurisdiction gives a standard rate and a
pricing MODE, and the mode is frozen onto `Order.PricesIncludeTax` at save the way `CurrencyType`
already is. Tax lives on LOCATION because that is what tax law is a function of — not on language,
not on how a customer pays. Null means "never located" and resolves to the home market (`CA-ON`,
tax-exclusive), so nothing about the installed base changed until a shop says where it is.

The change set arrived from outside a session and was reviewed the same day. It was right in shape
and right in the back-out arithmetic (`amount − amount ÷ (1 + rate)`), and wrong in two ways that
reached real money — both now fixed, and both worth keeping as warnings:

- **An inclusive location's `standardRatePercent` was read nowhere.** The reseed was guarded
  `reseedMatrix && !inclusive`, so the rate actually in force came from the per-method matrix that
  the *same branch hides*. Live proof: a shop located in `JP` (consumption tax 10%) carrying 13% on
  every method. Now an inclusive rate comes from the jurisdiction via
  `TaxJurisdictions.IncludedTaxRatePercent`, applies to both portions, ignores the per-method rules
  entirely — a value-added tax cannot vary by tender — and is STATED on screen where the matrix used
  to be. See `context.md`, "a setting whose value the UI hides".
- **Every breakdown derived tax as `Received − Deposit`,** which is structurally zero once the tax is
  inside the price, so a receipt printed "tax 0" twice beside a non-zero total. `SectionPayment` now
  carries `DepositTax`, `FinalTax`, `PricesIncludeTax` and `DepositStageTotal`, and the three
  consumers read them instead of re-deriving. Labels follow the mode too (`Order.Fields.IncludedTax`
  via `TaxLabelConverter`), because subtotal + tax ≠ total is correct here and reads as a defect.

**The tax NUMBER got the same treatment.** `"GST/HST"` was spelled into fifteen string-table values —
the settings label, the branding card title, the receipt line, in all five languages — so a shop in
Osaka read `税番号（GST/HST）` and printed a Canadian tax number's name on its own tax slip. A
jurisdiction now declares which number it issues (`TaxNumberLabel` → a `TaxNumber.<name>` key, grouped
by tax REGIME: `GstHst` for the three Canadian entries, `Vat` for FR/ES, `ChinaTaxpayer`,
`JapanInvoice`) or omits it, in which case Shop Settings does not ask for one at all — the US.
`Shop.Setup.TaxNumber` and `Branding.TaxNumber` were pruned as orphans. Two traps recorded in
`context.md`: this must NOT be inferred from `pricesIncludeTax` (Canada taxes consumption and quotes
exclusive, so inferring drops the home market), and a stored number must keep printing under a generic
label rather than vanish when a shop relocates.

Also fixed in the same pass: `pricesIncludeTax` made a REQUIRED parameter of
`CalculateSectionPayment` (it was optional, which is how a harness silently kept the old arithmetic);
`TaxJurisdictions.Default` forcing the load before reading the cached default code; a NEW shop seeded
from its location instead of only a re-picked one; the rate moved out of the five language files into
a `{0}` filled from the JSON, so editing a rate cannot leave a translation stale; a confirmation
before a location change discards a configured matrix — as a pure `WouldDiscardConfiguredRules`
predicate plus a thin prompt, because the `MessageBox` blocked the harness that drives the picker
(`context.md`, "a confirmation prompt inside an event handler"); and the picker moved off hand-built
`ComboBoxItem`s. New `taxcheck` harness (253 assertions) covers the presets, both pricing modes, the
names in every language, the upgrade path and the settings screen.

## Recent work (2026-07-27 → 07-28)

### The orders list is one line per cell, every row the same height
A single wrapping `TextBlock` was doing all the damage: the Custom Service column stacked the
garment names under the flag, so a row listing several garments was taller than its
neighbours — the one thing a list read by scanning down a column cannot afford, and
invisible in source until somebody's order has enough garments.

`ListCellText` in the theme (`NoWrap` + `CharacterEllipsis`, no size and no colour) is
now the one place that behaviour lives; `NumericCellText` derives from it. Three columns
came off `DisplayMemberBinding`, which generates a bare TextBlock that cannot be styled
— an over-long value was clipped mid-glyph with no ellipsis. Full values moved to
tooltips, and horizontal scrolling went from Disabled to Auto: with nothing wrapping, a
narrow window can only be answered by scrolling to the columns.

> **A horizontal StackPanel defeats `TextTrimming`.** It gives children infinite width,
> so a child never learns it overflowed. Use a Grid with a star column.

### An administrator can sign in as another user
A button in the account screen's identity card hands the session to the selected person.
It grants an administrator nothing new — they can already set anybody's password — what
it buys is SEEING the application as somebody else: which shops they get, which chrome
is hidden, what their language toggle offers.

Gated in the SERVICE, not only in the UI: the roster edits beside it write data, this
one hands out a session. Refused for a non-administrator, for yourself, and for an
account every shop has delisted — that last would spend the administrator's own session
to reach "no shop is available" and then the login screen. The bound shop is cleared, or
capabilities go on resolving against the shop the ADMINISTRATOR had open.

The window only REPORTS the choice; the caller performs it, because it owns the main
window that has to come down first. From the shop picker the switch becomes a THIRD
`ShopSelection` state — folding it into Cancelled would sign the new user straight out.

> **"Add an SVG icon" means `Path` geometry**, which is SVG path syntax. WPF renders it
> natively; an `.svg` file needs a rasterizer that is not installed here, and a bitmap
> will not stay crisp at every DPI.

### A person has a first and a last name, and a login that can be changed
`CredentialRecord.DisplayName` became `FirstName` + `LastName` (credentials.json schema
3 → 4). The greeting is now "Hi Tina"; the user-management list reads
`Tina Zhang (Manager, Staff)`; the detail pane shows the login and lets an administrator
change it — for every account EXCEPT the administrator's own.

> **The split rule is deliberately conservative.** No whitespace — "林艳", "Prince" —
> puts the whole value in `FirstName`. A Chinese name is family-name-first with no
> separator, so a positional guess would greet 林艳 as "林", by her surname alone.
> With whitespace, split at the LAST space. Lossless either way.

`PersonName` (Full / Label / Greeting) is the single composer. `Label` never returns
blank; `Greeting` is the first name. Known limitation, recorded rather than
half-solved: the join is given-name-first, the western order.

**Renaming a login has two traps.** `RefreshCurrentUser` identifies the session BY user
name, so after a rename the record no longer matches itself and the session kept a login
that no longer existed — decide "is this the signed-in account" *before* renaming. And
`CredentialFile.ProvisionedAccounts` must be **left alone**: it records which SEED NAMES
have been created and `ProvisionSeedAccounts` looks each seed name up in it directly, so
renaming the entry from `staff` to `sam` leaves `staff` unlisted and the next load seeds
a fresh `staff` **with a known password** beside the renamed one.

> That second one was got backwards first time round, with a confident comment saying so,
> and the harness agreed because it only ever renamed an account that was never seeded. A
> rename test that does not rename a SEEDED account proves nothing about it.

The administrator's login cannot be changed — a product rule, kept at the user's
instruction. Its box is DISABLED rather than read-only: a read-only box looks editable and
silently swallows typing, which reads as the application being broken. Seeding identifies
the administrator by its FLAG rather than its name, so "exactly one administrator" holds
structurally rather than resting on that one guard.

**One Save per screen.** The pane had two buttons both labelled Save Changes — the card's
saved the profile, the footer's saved only the password and roles — so editing a name and
pressing the obvious button discarded it on the reload, under a "changes were saved"
message. The footer's Save now applies the whole pane, profile first because it may
rename. Taken names are reported as they are typed, under the field they belong to, and
availability is settled *before* the rename confirmation.

### Hotfix: a fully-deposited order could not have its balance re-opened
Price 123 pre-tax, 13% card, a 123 deposit marked received. The section auto-cleared and the
master "clear all final balances" ticked — both wanted — but the master could not be UNTICKED.

> **Two independent things put it back**, which is why it looked unfixable from the UI. The
> auto-complete re-evaluated its rule on EVERY refresh rather than on entry into the state, and
> the master's own tick was driven by `IsOrderBalanceCleared()` — "is anything owed" — which is
> true for a fully-deposited section whatever the user ticked.

> **A control's state and the fact it describes are different questions.** Drive a checkbox from
> what the user has MARKED. The money model was left untouched: `IsSectionCleared`'s
> `FinalBase <= 0` rule feeds `FinalBalance`, the receipt and the list column, and changing it to
> satisfy a UI complaint would have re-priced history.

Requirement "the final payment type stays pickable at a zero balance" fell out of the first fix —
`IsSettled` gates on the tick — but is asserted rather than assumed.

The breakdown gained a DUE line per portion (the taxed figure, which is what the customer hands
over) with a RECEIVED partner that appears only once that portion is confirmed; 实收 became 已收.

> **Hide the received half until it is true.** Showing it from the start states money was taken
> when it was not, and a zero cannot be told apart from a portion that was genuinely free. Hide
> label WITH value — a lone label reads as a value that failed to load.

Shipped in the same hotfix: **receipts record who served the order** (`Order.LastModifiedBy`).

> **Store the rendered NAME, not a key, for anything printed.** Resolving it at print time would
> change what an old receipt says the day somebody is renamed and blank it the day they are
> deleted — and accounts live in credentials.json, outside the database, so there is no key to
> point at. Taken from the SESSION, never the form: "who saved this" is not a field anybody types.
> Omitted rather than printed empty on rows that predate the column.

*Harness traps worth keeping:* the snap-back arrived one recompute AFTER the click, so an
assertion taken immediately passed while the bug was fully present; and a new order opens with
the alteration category on "None", which switches the service off and makes every figure zero.

> **Adding a model column breaks harnesses two ways.** Those reading the LIVE database inherit
> whatever schema it has, and the guards only run at app startup — so run them against the live
> file after adding a column (`scratchpad/livemigrate`). And a fixture that migrates ITSELF must
> run EVERY guard: `headercheck` ran the Shops one alone and broke on the first Orders column
> added afterwards, having already been fixed once for the identical symptom.

### Currencies come from the installed languages, chosen in a panel of their own
The currency set is no longer a fixed list. Each `*.lang.xml` declares its market's currencies
under `Currency.Codes` — en-US `CAD,USD`, zh-CN `CNY`, fr-FR/es-ES `EUR`, ja-JP `JPY` — so
"adding a language is dropping a file in" now covers its money too.

> **Put the mapping in the language FILE, not in code.** The "English shows CAD and USD, CAD
> first" exception the request called out is then a value rather than a branch, and a build
> shipping en-CA instead needs no code change. Order is load-bearing: English's currencies
> lead the offer.

`CurrencyType` still BOUNDS what can be stored — the integers are a compatibility surface on
two tables — so a declared code the enum cannot name is dropped rather than guessed at. That
is the honest limit of "fully dynamic", and it cost EUR + JPY being added.

> **Not every currency has two decimal places.** JPY has no minor unit, so `¥1,695.00` is wrong
> in the same way the wrong symbol was. Symbol and digits are one fact about a currency and are
> formatted together; splitting them is how `{symbol}{x:N2}` ended up hand-written at four sites.

`ShopLocalizationWindow` replaced two tick lists and two pickers inline in Shop Settings, which
now shows a link card summarising the choice. Languages left, a card per ticked language right.

> **One fact, one row object.** EUR is reachable under both Français and Español; the two cards
> share ONE row. Two independent tick boxes for one currency can disagree, and then there is no
> answer to "does this shop take euros".

*Two traps worth keeping:* an unresolved string key renders AS the key, so adding enum members
without their table entries put "CurrencyType.EUR" on screen — it reads as a broken control, not
a missing translation. And `ComboBoxItem`s added to `Items` in a constructor log four binding
errors each, having no `ItemsControl` ancestor to resolve the stock template's alignment
bindings against; use `ItemsSource` and let the ComboBox generate its containers.

### A shop accepts 1..N currencies; an order records the one it was priced in
Asked for as "the same as store languages, for currencies". It is not the same, and the
difference decided the design: language is how a screen READS, currency is a fact about the
order. So there is no per-user override (an administrator sees every language; nobody prices
outside the shop's set) and the money model had to change with it.

> **A shop's setting describes TODAY; an order's column describes when it was priced.** Every
> amount on screen read `CurrencySettingService.Instance.Symbol`, so the first shop to accept
> a second currency would have reprinted its whole history in it — ￥1,695 as "$1,695.00".

Two latent defects were found in the survey and both were fatal the moment a shop had a
second currency: display never read the order, and `OrderEditWindow` never WROTE
`Order.CurrencyType`, so every order ever saved carried the enum default regardless of its
shop — all 44 in the CNY shop included.

> **A column that is never written is not a spare column, it is a landmine.** Anything that
> starts honouring a dormant column needs a backfill in the same change. Pin that repair to
> the arrival of the column that motivated it, never to startup: it is safe only because
> "CAD" could not mean anything but "unset", which stops being true the instant the editor
> starts saving it.

`Services/ShopCurrencies` owns the rule, shaped like `ShopLanguages` with the same never-empty
fallback. Stored as enum NAMES, not integers — reordering the enum would otherwise silently
re-denominate every shop. The editor keeps an order's own currency in its picker even after
the shop drops it: what a shop takes today does not reach back and restate what it charged.

*Worth knowing:* every `CurrencyAmountConverter` binding in the XAML **already** passed the
order's currency as `values[1]` and the converter discarded it. The list and the whole detail
panel were one line. Check for existing plumbing before building it.

### Every window fits the screen it opens on
`OrderEditWindow` declared `MinHeight="900"` against a 752-tall work area, so the pinned
Cancel/Save footer sat 148px below the desktop and **could not be dragged into view** —
saving an order was impossible, not merely awkward. Six other windows opened taller than
such a screen without being unusable.

> **A window minimum is a FLOOR WPF honours against the desktop, not a preference.** The
> layout was never wrong (`Auto` title / `*` ScrollViewer / `Auto` footer is exactly
> right); the window just asserted a minimum bigger than the display. Check any new
> `MinHeight` against 728 — a 1366×768 laptop.

`Controls/WindowFitting` scales the whole layout down proportionally, registered from
`App` as a `Window.Loaded` **class handler** so a window added later is covered without
opting in — the per-window alternative fails by omission, which is how this shipped.

> **`LayoutTransform`, never `RenderTransform`.** Only a layout transform makes the content
> MEASURE smaller, which is what lets the minimum come down. A render transform looks
> identical in a screenshot while the window goes on demanding its full height — the bug
> would appear fixed and not be.

Scale comes from the declared MINIMUM (the author's "below this it breaks"), never the
design size; never scales up; floors at 0.5. Measured: editor at 0.820 on this machine,
Save button bottom at y=725 against a work area ending at 752.

> **`PointToScreen` returns DEVICE pixels; every WPF size is device-independent.** On a
> 150% display a correctly-placed button reported y=1087 against a 752 work area and read
> as broken — 1087 device px is 725 DIP. The dangerous direction is the other one: raw
> comparison passes on a 100% monitor, so a genuinely broken layout would look fine.

*Measured rather than assumed:* a `Popup` DOES inherit an ancestor `LayoutTransform` for
rendering (drop-down items at 0.821 under a 0.820 window). The separate-visual-tree rule
that defeats *bindings* across a popup boundary does not carry over to rendering.

> **The screen is an INPUT, not ambient state.** The harness read
> `SystemParameters.WorkArea`, passed on the 1280×752 laptop it was written on, and failed on
> a 2057×1323 desktop days later — not because fitting broke, but because nothing needed
> fitting and every assertion had gone vacuous. `Fit` now takes a `(Window, Rect)` overload
> and the monitor-reading one is the wrapper. Any rule whose input is "the machine you happen
> to be on" needs that seam or it can only be tested on one machine.

### Every comment in the application is English
62 comments across 25 files named a menu or a field by its Chinese label. The rule was
already in `SKILL.md` and had been broken steadily anyway — which is the point worth
keeping:

> **This rule erodes quietly.** Not one of those comments was careless; each named the
> label the developer had just been looking at, and read perfectly at the time. It is only
> visible in aggregate, so review never catches it and a periodic grep does.

Rewritten rather than deleted — a comment saying "drives the Custom Service list flag" carries
real information. Naming the thing by its **key** (`Order.Fields.CustomMadeFlag`) is
strictly better than either label, since keys are greppable and survive a re-wording;
navigation paths took English menu labels instead, which read better than a pair of keys.

`SKILL.md` now separates the two audiences explicitly (answering the user in their
language is a courtesy to one reader; the repo serves every future one), names the trap
that a task *about* Chinese text is not licence to comment in Chinese, and carries a grep
to run before finishing. A rule with no check is a preference.

Untouched on purpose: the zh-CN/ja-JP tables, language names in prose, and punctuation
quoted to describe it — that is data, not writing in it.

### Japanese, added as a fifth language — the claim, now cheap
Cost the file and its seed data, nothing else. Parity was exact first time, and the
discovery count and both completeness sweeps picked ja-JP up with **zero harness edits** —
the return on generalising them during the Spanish round, one language later.

Worth doing rather than a second Latin language because it is the **second CJK** one, and
so the first real test that punctuation is *data*: `、` with no trailing space, fullwidth
`（）`, corner brackets `「」`. `Format.ListSeparator` now punctuates three ways across
zh / ja / en.

> **The generic "differs from English" sweep cannot check punctuation.** `", "` and `"、"`
> differ whether the translation is right or wrong, so a language whose separator was
> copied from English would pass. Punctuation shapes need asserting by value, per language.

*Found while seeding, unrelated to Japanese:* printing each shop's name in **every**
language rather than only the new one showed Vancouver had no French name, so a French
reader had been seeing its Chinese name since fr-FR shipped. Invisible because the fallback
renders something. Now a standing habit in `SKILL.md` §1a — report all languages, but do
not assert, since a user-created record may legitimately carry only one.

### Spanish, added as a fourth language — the "drop a file in" claim, re-tested
The claim held for **code** and broke for **data**. No `.cs`, no `.xaml`, no `.csproj`
edit: the csproj globs `Settings\**\*`, every language list is built from
`AvailableLanguages`, and the PDF suffix derives from the BCP-47 primary subtag, so
`es-ES` exported as `Measurements_es.pdf` without being told to.

What did need work is invisible from the code, and is the thing to remember when a
fifth language arrives:

> **`Shop.InstalledLanguagesJson` stores an explicit list, so "installs all of them"
> was never a value — only a snapshot.** The shop that installed all three silently
> became a shop installing three of four.
>
> **Every existing shop is nameless in a newly added language**, and `ResolveName`
> falls back to `values.Values.FirstOrDefault(…)` — *dictionary insertion order, not
> English*. Vancouver's first stored name is Chinese, so a Spanish reader saw
> 温哥华工作室. Fix it with data; re-ordering the fallback would change what every
> other language falls back to.
>
> **A genuine cognate is not an untranslated string.** Spanish spells `Branding.Color`
> "Color" and `Order.Fields.Subtotal` "Subtotal". The exemption is keyed on
> *(key, language)*, not the shared-across-all-languages list — that one would also
> stop anyone noticing the same key untranslated in French. Padding the Spanish out to
> satisfy a test would put worse Spanish on screen.

Also paid once and removed: `formatcheck` hard-coded `AvailableLanguages.Count == 3`,
so a new language failed tests that had nothing to say about it. Counts now come from
the folder and the per-language sweeps iterate the discovered set, so language five
costs no harness edit.

**The removal side, which had no coverage at all.** Test scope for any language
add/removal is now fixed in `SKILL.md` §1a — key parity, translation precision, and the
all-languages-deleted case, and *not* a full application re-test.

> **Where a load guard sits decides whether a failed load is destructive.** An empty
> folder is caught by the file-count check that runs BEFORE `Load`, so the loaded table
> survives. Files that parse but declare no `code`/`name` get INTO `Load`, which clears
> the table before it can know the load will fail — every key then renders as itself.
> Harmless while startup aborts either way; it is exactly what an in-app "reload
> languages" would have to work around.

Removing one language: the picker drops it, `SetLanguage` refuses it and leaves the
current language alone, and a shop whose only installed language was removed still
resolves to something rather than opening unrenderable. With every language gone there
is no graceful answer — an app with no string table cannot even apologise in the user's
language — so the requirement is to **fail loudly and name the folder**, which is what
`OnStartup`'s try/catch → unlocalized MessageBox → `Shutdown(1)` does.

### Test shops now cover every language shape
Five shops on the developer machine, chosen so each branch of `ShopLanguages` has
something real to exercise — 1, 2, 3 and 4 installed languages all represented:
#1 LeeYonge zh+en, #2 Tianbao **all four**, #3 Vancouver **es+en opening in es-ES**
(a shop that opens in a language added after the app shipped), #4 Montréal fr+en+es
(Spanish as an ordinary set member, not only as part of "everything"), **#5 Toronto
Bespoke en only with 40 orders** — the remaining hidden-toggle case
(`scratchpad/englishshop`, modelled on `frenchshop`). #5 also takes the fourth
numbering mode, YearlySequential, so all four are in use.

A shop's NAME and ADDRESS stay per language even when it runs in one: what a shop
runs in and what it is called are different questions, and an administrator working
in Chinese should read a Chinese name for an English-only branch.

> Seeded orders back-dated across a year boundary make a YEARLY counter restart, so
> #5 legitimately has two series (4 in 2025, 36 in 2026). No duplicates — `Reserve`
> scans for numbers already taken, which is what makes a thrashing counter safe. A
> seeder reporting "first … last by id" hid this; report the DISTINCT count.

### A shop installs 1..N languages; the toggle follows the shop, not the role
Language choice used to be a pure capability: administrators could switch, nobody
else could. Now a shop declares which of the shipped languages it runs in, and its
managers and staff switch between exactly those — hidden entirely at one, because a
picker with a single option is chrome that cannot do anything. An administrator keeps
all of them, since they work across branches.

`Services/ShopLanguages` is the only place the rule lives, consumed by four surfaces
(toolbar toggle, shop editor, measurement print dialog, PDF download panel). It sits
outside both `AuthenticationService` and `ShopContext` because the answer is a product
of both — a capability and a shop's configuration.

> **The fallback is what made this shippable without a data migration.** A shop with
> nothing installed reads back as just its `PreferredLanguageCode`: one language, no
> toggle, exactly the old behaviour. A shop that has said nothing at all has
> restricted nothing, so it gets everything. Both are the shop's own statement read
> literally, and no existing branch changes until somebody installs a second language.

Two names carry weight. `CanChooseLanguage` became **`CanChooseAnyLanguage`** — under
the old name `false` read as "no toggle", which stopped being true. And the language a
shop opens in resolves through `ShopLanguages.PreferredCode`, never
`shop.PreferredLanguageCode` directly: the two can disagree, and opening a branch in a
language its own toggle cannot return to is worse than either.

The editor enforces "opens in a language it installs" by what the picker CONTAINS —
it lists only the ticked languages — rather than validating the pair afterwards.
Opening a shop keeps the language already on screen whenever the shop installs it, so
a staff member who picked English at login is no longer overridden by a shop that
runs in English.

**Where the installed set is surfaced:** under the greeting in the main window, and on
each card in the shop picker (`CAD · 简体中文, English · 37 orders`). The picker card's
language slot used to hold the shop's PREFERRED language, so a bilingual branch
advertised exactly one — the installed set is strictly more informative and is what a
manager or staff member will actually be able to switch between once inside. Plain
text rather than chips, because languages are discovered and an ellipsizing strip
degrades predictably where a growing stack of badges would resize every card.

### Cancel in the shop picker means "go back", not "quit"
It called `Shutdown()`, on both the startup and the sign-out path. Sign-in and shop
selection read as one flow, so Cancel on the second step is taken to mean "back to
the first" — and instead the application disappeared, with no way to hand the machine
to a colleague short of relaunching it.

`App.OpenShopOrSignInAgainAsync` now loops: open a shop, or sign out and show sign-in
again. Both call sites share it. `Shutdown()` is reached only when the LOGIN window is
dismissed, which is the one gesture that unambiguously means "I am done".

Signing out is the point, not a side effect: the session is already authenticated when
the picker appears, so returning to sign-in without it would leave the previous user's
session live behind the login window.

> General shape: a Cancel that has a previous step to return to should return to it.
> Only the first step's Cancel may end the application.

### Keyboard paging on the order list
Left/Right page the list from anywhere in the main window. Paging was previously
reachable only by clicking two small buttons under it.

**A window-wide arrow shortcut must be a `PreviewKeyDown` handler, never a
`KeyBinding`.** An InputBinding fires whatever has focus, so the list would page
every time somebody moved the caret in the search box — the shortcut would have made
the app *less* usable. The handler stands down for controls that own the horizontal
arrows (`TextBoxBase`, `PasswordBox`, `ComboBox`, `DatePicker`, `Slider`, `MenuBase`),
walking UP from the focused element because focus lands on a part *inside* those
controls. It also stands down for any modifier: Alt+Left is "back" and Ctrl+Left is
word-wise caret movement.

The rest is what makes it an accessibility change rather than a shortcut: the page
summary is a polite live region with `LiveRegionChanged` raised explicitly (rebinding
the text does not raise it), and focus moves to the first row of the new page — without
that a keyboard user lands on a page whose rows they cannot reach until they Tab back
in, and a screen reader has nothing to read.

### Contact details on every account
`PhoneNumber` / `Email` on `CredentialRecord` — **account-level, not per
membership**: one person working at two branches has one phone and one mailbox.
Nullable, so existing credential files are already valid; no migration.

Editable in two places on purpose. Store Members reaches people who belong to a shop;
`CreateAccount` deliberately makes accounts that belong to none, which the roster
cannot reach at all, so User Management has a matching card over
`UpdateAccountContact`. That call touches no membership, so unlike a role change it
is safe on the administrator and on one's own account.

Validation lives in `Models/ContactValidation`, shared with the order form rather
than copied — an address the roster accepts but the order form rejects is a defect
nobody sees until mail bounces. Blank is valid and persists as `null`, never `""`:
two spellings of "no phone number" print differently depending on the reader.

### Every language list is discovered, never listed
The download-measurement picker was two literal radios plus
`IsChecked ? "en-US" : "zh-CN"`, so French shipped as a system language that
measurements could not be exported in — while the PRINT dialog beside it was already
dynamic. Every such list is now built from a discovered set (since superseded by
`ShopLanguages`, which narrows it to what the shop installs).

Each option is labelled with the language's OWN name from its own file, so a new
language names itself instead of needing a translated entry added to every existing
file. Adding a language stays "drop a file in", which is the point of the split.

> When testing a "supports N languages" claim, assert the install HAS more than two
> first. Without that guard the whole check passes vacuously on a two-language
> install and proves nothing.

### The measurements PDF — rebuilt, and moved out of the window
Composed into `page.Content()`, the letterhead rendered **once**: a one-page sheet
looked right, a two-page sheet carried branding on page one alone with the footer
stranded wherever the last garment ended. Only `page.Header()` / `page.Footer()`
repeat. Rendering a two-page sheet then exposed a second fault — a garment name at
the foot of one page with its measurements orphaned overleaf. Wrapping heading and
table in one `column.Item().Column(...)` does **not** make them atomic; the name is
now the table's `Header` row, which QuestPDF repeats per page.

The layout lives in `Services/MeasurementSheetDocument`, taking plain
already-localized data (no string keys — the sheet is generated in the language
picked in the print dialog, not the UI language). It left the window because a
window needs a message loop, so a layout inside one can only be checked by a human
clicking Export.

Visual: page numbers, a bordered card for the order details, accent-barred garment
headings, striped rows. The colon belongs to the label — as `": 9051234567"` it
read as a missing field name. Info labels 132pt, garment terms 190pt, because a
term name runs ~25% longer in French and a wrapped label costs more than a gap.

**The letterhead itself is `Services/ShopLetterhead`** — name, subtitle, contact
lines, tax line, as plain strings resolved for an explicitly passed language (the
sheet is generated in the language chosen in the print dialog, not the UI one).
Receipt, printed sheet and PDF all build from it, because they had drifted: the
receipt grew a letterhead while both measurement paths went on injecting the GST/HST
number at the top of the page, so a sheet opened with a bare "GST/HST 税号：…" above
its own title and never named the shop.

Its rules, all taken from the receipt:
- the tax number is the **last** letterhead line, never the first;
- a custom header **replaces** the generated letterhead rather than stacking on it —
  a shop that typed its address into the editor must not also get the shop record's
  address printed underneath;
- the document title is the letterhead's subtitle, and moves into the **body** when a
  custom header replaces the letterhead — in both formats, so print and download stay
  structurally identical whether or not branding is configured.

`ResolveTaxRegistrationNumber` moved to `ReceiptBrandingStore`: the receipt and the
PDF both print it and each had its own copy of the override rule.

### Bug: printing measurements in inches produced an empty sheet — FIXED
`CustomMadeMeasurementReader` read `value.In` directly and skipped any row where it
was blank. A value carries both units only if the editor's cm/inch toggle happened
to be flipped while it was on screen — measured on the live database, **768 of 768
stored values had a cm figure and only 39 had an inch one**, so 95% of rows were
dropped and any order never toggled printed nothing at all.

Now `Models/MeasurementUnits` converts from whichever unit WAS filled in. It owns
the conversion for the editor, the printed sheet and the QuestPDF export alike —
they had separate copies, which is how the print path came to disagree. The
trailing `+`/`-` a tailor writes is carried through untouched; free text is
returned unchanged rather than reinterpreted.

Verified against every real order: 111 sections / 768 rows in **both** units, 0
orders empty in inches.

### Product catalogue + receipt letterhead — DONE
Ready-made categories were a `static readonly string[]`, so every shop sold the
same five things. Now `ProductCatalogService`, per shop, modelled on
`MeasurementTermsService`; managed at Local Configuration → Product Categories, seeded from shipped
defaults, with add / rename / remove / reorder / restore.

The shipped ids (`Jackets`, `TiesBowtie`, …) are a **compatibility surface** — every
saved order stores one, and each is a `ClothingItem.<id>` string-table key.
Predefined entries keep their ids and take names from the string table, so they
stay translated into languages added later; only user-added ones carry their own
names. `ResolveName` always resolves, including for a deleted category.

Receipt letterhead: title, subtitle, GST/HST and logo default all **left aligned**;
shop address / phone / email / website each on their own labelled line via the
existing `ReceiptInfoLine` and `Shop.Setup.*` keys (the shop's field names, not the
customer's `Order.Fields.*`). `Shop.TaxRegistrationNumber` added; the header/footer
editor's number overrides it, being the more specific surface.

*Found by rendering it:* the tax number printed **above** the shop's own name — it
was inserted at document top, which is right only when a custom header replaces the
letterhead.

### French as a third system language — DONE
Full fr-FR set. Proved the per-language split: a file was dropped in and nothing
else changed. Seeded test shop #4 *Atelier Montréal* (fr-FR, CAD, MTL-0001…0040).

Two faults the harness caught that review would not: a translation word-identical
to English is indistinguishable from a missing key falling back; and French runs
~25% longer than English, truncating fixed-width column headers.

### Sonar to zero — DONE
All 31 standing findings cleared. S2325 on event handlers splits by wiring: a
handler named in XAML **cannot** be static (generated `InitializeComponent` emits
`this.Handler`); one attached only from code can. S125 on a comment is almost
always prose that parses as code — reword, don't suppress.

*Bug fixed:* `\"{0}\"` in the delete dialog. XML has no backslash escaping, so users
saw literal backslashes. Present in both languages since forever.

### Systematic config refactor, phases 0–3 — DONE
- **0** — language punctuation became data (`Format.ListSeparator`). Replaced
  `code.StartsWith("zh") ? "、" : ", "` duplicated across **five** files.
- **1** — `Settings/System/`, one file per language, discovery, and **key-parity
  detection** (without which splitting is a downgrade: a missing key becomes a
  silent fallback).
- **2** — the last unlocalized strings. Smaller than scoped: the startup and
  data-folder failures are unlocalized *on purpose* (they run before the string
  table loads) and must stay that way.
- **3** — `UserDataPaths`, the single definition of the data folder, which had been
  spelled out in six services. `Config/` and `Backups/` with lazy, fall-back-safe
  migration and a retention count in `app-defaults.json`.

*Bug the JSON DTO warning uncovered:* `System.Text.Json` matches property names
case-**sensitively** by default, so hand-written `"defaultLanguage"` never bound and
`AppDefaults` always returned its fallback. The test passed only because the
fallback matched the file.

### UI work — DONE
Shop address under the header title; login no longer pre-fills `admin` (the sign-in
error deliberately refuses to distinguish unknown user from wrong password, and
pre-filling handed that away); login language field stacked over its box; nav bar
order settled as greeting · Local Configuration · Language · Store Members · Sign Out; right-click menu themed;
theme, typography and panel transitions modularised.

Measurement-term gender picker: three radios → a drop-down. Radios need the width
of **every** label at once — measured at ~291 px in Chinese (fits a 420 px dialog),
~429 px in English and ~463 px in French (both overflow). Symbols reuse the ♂/♀
characters the terms list already badges with, via the shared
`MeasurementGenderPresentation`.

> A right-anchored Local Configuration menu was built and then reverted — the caret and content
> flipped sides and it read worse. Local Configuration and Store Members were swapped instead. Do not
> re-attempt the mirrored menu.

---

## Earlier work — index only

Everything below predates this condensing pass and is summarised here **by title
only**. These entries are not reconstructed from memory: read the dated entry in
`TODO.md` if one becomes relevant. (Per Step D: an honest pointer beats a plausible
summary.)

**2026-07-27** — multi-shop user & role management · store members panel · per-shop
payment/tax rules and receipt numbering · app-wide theme and receipt layout, mock
data · seeded-garments bug · rebrand LeeYongeOrdering → CameywareOrder · Kestrel
port-in-use startup failure · both quality gates cleared.

**2026-07-26** — multi-shop + login groundwork · global settings export/import ·
per-stage tax rates and payment breakdown rows · pick-up confirmation for unpriced
services · deposit ceiling and shared money-input behaviour · several payment and
read-only-state bug fixes · string-table audit · README.

**2026-07-25** — measurement terms system · custom-service flag column and
measurement printing · cancelled/returned refund state · import/export menu.

**2026-07-24** — records list → ListView/GridView · document upload for custom-made
records · payment section locking when a balance is cleared · currency per-order →
global · Local Configuration menu · per-portion payment tax split · app icon and welcome header.

**2026-07-23** — alteration category dropdown · cm/inch toggle and localized
measurement download · order locking and status filter · detail-panel pricing ·
first workspace-wide Sonar cleanup.
