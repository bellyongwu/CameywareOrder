# Context — CameywareOrder

Running project state, recent decisions, and gotchas. Update as work proceeds.
Read this (with `TODO.md` and `Architecture.md`) before starting any task.

## Workspace

- **Renamed 2026-07-27: LeeYongeOrdering → CameywareOrder** (product is now developed by
  Cameyware INC). Namespace, assembly, exe, `.csproj`/`.sln` and the LocalAppData folder all
  carry the new name. Two things deliberately did NOT change — see the rebrand entry in
  `TODO.md`: the **repo directory** (still `LeeYongeOrdering`, renaming it would break open
  editors and local paths for no gain) and **`Main.HeaderTitle`** (上海丽扬高级定制 /
  "Shanghai LeeYonge Bespoke" — that is the customer shop's name, not the product's).
- Repo: `c:\Projects\LeeYongeOrdering` — directory name is intentionally stale, everything
  inside it is `CameywareOrder`. Older TODO entries quote a `d:\` path from a past move.
- App process name (kill before building): `CameywareOrder`
- Build/verify command:
  ```powershell
  Get-Process -Name CameywareOrder -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 400
  dotnet build CameywareOrder.csproj -v quiet --nologo 2>&1 | Select-String -Pattern "error|Build succeeded|Build FAILED"
  ```
  Expect `Build succeeded. 0 Error(s)`.

## Recent decisions / state

- **A mark that the ELLIPSIS eats is not a mark (2026-08-02, v9.2.1).** Copy Order suffixes the
  copy's customer name (`- Copy 1`). Every assertion passed and the list still could not tell a copy
  from its source: the column was 170px with `CharacterEllipsis`, which trims from the END, so
  "Priya Raghunathan - Copy 1" rendered as "Priya Raghunatha…" — visually a *truncation of the
  original*, which is worse than no mark at all. Widened to 240. Generalises: when a feature's whole
  point is a suffix, check what the surface showing it does with overflow, and check it by RENDERING.
  - Residual and known: a name long enough to overflow 240 still trims, as every long value in the
    list does. The tooltip carries the whole of it. Reported to the user rather than fixed.
  - The render only found it because the fixture had ROWS. `uicheck` had been screenshotting the main
    window against an empty list, which proves the chrome and nothing about a cell. It now seeds two
    live orders and copies them through the real command.
- **Compose and STRIP the same suffix in one type, and read every language's format (2026-08-02).**
  `OrderCopyName` does both. Split across two places they drift, and the failure is silent: copying a
  copy produces "X - Copy 1 - Copy 1" and the number stops describing anything. The strip must read
  EVERY shipped language's format, not the current one — the name is one stored string, written by
  whoever made the first copy in whatever language they had on screen. Same reason the numbering scan
  starts past the highest index already in use rather than at 1: a Chinese-suffixed copy and an
  English-suffixed one are different STRINGS, so a plain collision test finds "1" free and hands out
  a second first-copy.
  - The `ILocalizedText`-not-`LocalizationService` rule has a real exception here. "Which language do
    I write in" is the interface's question; "what can this value look like in ANY language" is the
    SERVICE's, and no single language can answer it. One method (`ShippedSuffixFormats`) reaches the
    singleton and is documented as the seam.
  - The mark went on the CUSTOMER NAME, not the order number — the user's call, and the right one:
    the number is drawn from the shop's receipt run and printed on a slip somebody walks out with.
- **A harness that dereferences a fixture it did not verify reports a CRASH instead of the assertion
  (2026-08-02).** Proving the copy-naming checks could fail turned the "Copy 4" lookup null, and
  `binned!.Value` threw — burying three real FAILs under a stack trace. Guard the fixture and Check
  the guard, so a broken run still names what broke.
- **A `Window`'s code-behind belongs under `Views/`, whatever it contains (2026-08-02).** Asked to
  file the MainWindow partials under `Services/Main/`, they went to `Views/Main/` instead. Two
  reasons, and the first is mechanical: the SDK pairs `Foo.xaml` with `Foo.xaml.cs` by their being in
  the SAME folder, so separating them loses the designer association and the `DependentUpon` nesting.
  The second is the folder scheme itself — `Views`/`Models`/`Services` answer "whose is this?", and a
  partial of a Window is a view even when one file happens not to touch a control.
  - The honest test for "is this really a service" is **does it touch an `x:Name` control**. Measured
    across the five partials: Session 31, OrderList 17, DataTools 0, Printing 0, Receipt 0. Only
    `Receipt` is genuinely control-free AND state-free (one field, `_localization`), which makes it
    the one that could become a real `Services/StoreManagement` type beside `MeasurementSheetDocument`.
    DataTools and Printing use no controls but do use `_viewModel`, `_scopeFactory` and modal dialogs
    — view orchestration, not services.
  - Nothing referenced MainWindow by PATH (no pack URI, no `StartupUri`, no csproj item), which is
    what made the move safe — the same check the v5.0.0 folder split ran first. And `obj/` was
    deleted afterwards, or the generated partials at the old paths compile alongside the new ones.
- **Split an overgrown code-behind by MEMBER NAME, never by line range (2026-08-02).** The first
  attempt cut `OrderEditWindow` at line numbers taken from a grep that had matched only some
  signatures, so the boundaries between them were guesses — it sliced through method bodies and
  produced eleven syntax errors. The working tool walks the class body tracking brace depth (with
  strings, chars and comments scrubbed first, or a `{` inside a literal shifts every boundary after
  it), collects whole members, and selects them BY NAME. A name does not drift when anything above it
  changes, and an unknown name FAILS THE RUN rather than silently leaving the member behind.
  - Its own trap: the name-finder first matched prose inside a multi-line `[SuppressMessage]`
    justification — `"Named from XAML (EventSetter …)"` yielded the member name `XAML`. Anchor the
    match to a line that BEGINS with a declaration keyword.
  - The generated partials need their own `using` block, and it will not be the original's: three
    files failed on `TextBoxBase`, `Converters` and `AsNoTracking` because those usings served
    members that moved elsewhere. Expect one build round to settle them.
  - Reconcile the line counts (moved + kept == original). A splitter that loses or duplicates a
    member can still compile.
- **PARTIALS, not extracted classes, for WPF code-behind (2026-08-02).** Almost every method in these
  files touches `x:Name` controls that live on the generated partial. Extracting them into a helper
  type means threading dozens of controls through a signature, and SonarLint's single-file pass
  cannot see the generated fields so it flags every extracted method S2325 (SKILL §9a says the same,
  from the other direction). A partial file moves the code without moving what it can reach, and
  `Click="OnX"` keeps resolving because it is still one class. Reserve real extraction for logic that
  touches no control — which in these two files was almost nothing.
- **A ROLE IS A NAME; WHAT IT ALLOWS IS PER SHOP (2026-08-02, v9.0).** This REVERSES the v7.0.0
  decision recorded as "ONE installation-wide catalog … per-shop role definitions were considered and
  rejected". The reason it was reversed: a branch that also runs the workshop and a concession counter
  cannot share one definition of Manager, and the only way out under the old model was a second role
  with a second name for the same job — which is worse, because a person moving between branches then
  changes title.
  - `RoleRecord.ShopInstances` is `shopPublicId → capabilities`, and **absent means "use the
    default"**. That is what makes it additive: every role written before it has an empty dictionary
    and behaves exactly as it did, so the upgrade needs no migration and changes nothing until
    somebody varies one. Same fallback rule as `Shop.InstalledLanguagesJson`.
  - Keyed on `PublicId`, never `Shop.Id` — the local autoincrement is reassigned by a database
    import, and a key that moved would hand one branch another branch's permissions.
  - `AuthenticationService` resolves each membership against ITS OWN shop, not against the open one:
    an installation-scoped capability is answered by every active membership, and reading them all
    against whichever shop happens to be open would grant one branch's variation everywhere.
  - The administrator can never carry an instance. It is regenerated rather than stored (defined as
    "every capability there is"), so a branch able to narrow it is a branch able to lock the
    installation's owner out.
  - Deleting a shop must call `RolePermissionStore.DropShop`. Per-shop state living inside another
    file is not swept by the per-shop FILE deletion, and a restored archive with the same `PublicId`
    would otherwise inherit permissions nobody remembers setting.
- **An indicator needs a MINIMUM visible time, or fast work reads as no work (2026-08-02).** Most
  operations here finish inside one frame, so the busy overlay appeared and vanished without ever
  being seen and the screen looked like it had ignored the click. `BusyTracker.MinimumVisible` holds
  it 250 ms. Two things that make it honest: the hold is on the INDICATOR only — the data is already
  written and nothing waits — and `IsBusy` has to include the hold, or the last scope's dispose hides
  the overlay and leaves the timer with nothing to keep up.
- **A `node -e` script through bash is mangled exactly as a commit heredoc is (2026-08-02).** The
  skill's rule about `git commit -F` generalises to every multi-line script with quoting in it:
  apostrophes, guillemets and CJK brackets all came back as shell syntax errors, and one run silently
  wrote a broken TODO entry. Write the script to a FILE with the Write tool and run the file.
- **A Storyboard inside a ControlTemplate trigger must be FREEZABLE, so it cannot contain a Binding
  (2026-08-02).** `ThemedProgressBar`'s indeterminate sweep animated `To="{Binding ActualWidth,
  ElementName=TemplateRoot}"` so it would look the same at any width. WPF freezes template
  storyboards when it seals the dictionary, and a Binding makes that impossible: it threw
  `Cannot freeze this Storyboard timeline tree` and took the whole main window down AT LOAD. Use a
  literal. And do not over-compensate — a range of 620 on a 240-wide bar left the runner clipped and
  invisible for more than half the cycle, which reads as a bar that does not work.
  - **Both faults were found by RENDERING, not by building.** The build was 0/0 for each of them.
- **A reusable control must not reach the theme with `StaticResource` (2026-08-02).** `BusyOverlay`
  referenced `{StaticResource ThemedProgressBar}`; a `StaticResource` in a UserControl's own XAML is
  resolved while that control is being sealed, before the application dictionary is on the lookup
  path, and throws at load. The fix that is better than `DynamicResource`: name no style at all and
  let the theme's IMPLICIT `TargetType` style reach it through the live tree — it then cannot drift
  from what the theme says the control looks like.
- **`dotnet build` after deleting `obj/` fails until the build SERVER is shut down (2026-08-02).**
  MSBuild reuses worker nodes between invocations, and a node that remembers the old `obj` reports
  every WPF `*.g.cs` as missing — sixteen `CS2001`s naming files the markup pass should have just
  written. It is not a code fault and re-deleting `obj` does not clear it.
  `dotnet build-server shutdown`, then delete, then build with `-nodeReuse:false`.
- **Busy state is COUNTED, not a bool (2026-08-02).** `BusyTracker.Begin` returns a scope and the
  overlay lifts when the last one is disposed. Operations here overlap — a copy ends by reloading the
  list, and a shop switch can start a refresh underneath it — and with a flag the first to finish
  clears the indicator while the second is still writing. A scope rather than paired Begin/End calls
  so an exception cannot leave a progress bar on screen for work that stopped.
- **Adding a second condition to a QUERY FILTER changes every `IgnoreQueryFilters()` caller at once
  (2026-08-02).** `Orders` was filtered on `ShopId`; v8.0 made it `ShopId && DeletedOnUtc == null`.
  The escape hatch is all-or-nothing, so every existing caller — which had reached for it to drop the
  SHOP half — silently began seeing recycled rows too. All five were re-read and each now restates by
  hand whichever half it still meant. One was a real defect that nothing else would have found:
  - **`OrderNumberFormatter.IsTaken` asked through the filter**, so a binned order's receipt number
    read as FREE and would have been handed to the next order — two orders with one number the moment
    the first was restored, and that number is on a slip the customer is carrying. It now ignores the
    filter and restates the SHOP condition, because receipt runs are per shop: a global check would
    make a second branch skip past the first's numbers for no reason anybody could see.
  - The other four: the shop's order count, the picker's per-shop count and the archive export all
    exclude binned rows (a shop's "12 orders" must mean the twelve its list shows, and a restore must
    not resurrect somebody's bin on another machine); the shop DELETE keeps taking everything.
  - Generalises: before adding a condition to a filter, grep for the escape hatch and read every hit.
    The compiler cannot help here — every one of them still compiles and still runs.
- **An assertion about a collision must be driven at ONE INSTANT (2026-08-02).** The first version of
  the receipt-number check binned a four-month-old order and reserved a number today. It passed
  before the fix and after it, because a timestamp number composed today never collides with one
  composed in April — a fixture sitting on a fallback path, testing nothing. Reserving twice from the
  SAME moment is what exercises the scan. Watch for this shape wherever a test's own inputs make the
  failure mode unreachable.
- **Delete became reversible, so the WORDING had to move with it (2026-08-02).** `Delete.ConfirmMessage`
  said "this action cannot be undone" and now says the order moves to the recycle bin. A confirmation
  describing behaviour the application no longer has is worse than none: the next person to read it
  trusts the message over the code. Conversely the bin's own destructive card says plainly that
  nothing there can be recovered, because that is now the only place where it is true.
- **Two settings that a user experiences as ONE question share a store and a panel (2026-08-02).**
  Backup cadence/retention and recycle-bin retention are both "what happens if something goes wrong".
  Split across two panels, each answers half. `DataProtectionSettings` is per INSTALLATION — how much
  disk to spend on safety copies is a property of the machine, not of a shop, and a shop carried to
  another PC must not bring the old machine's schedule with it.
- **A backup runs at STARTUP, after the migrations and before the first window (2026-08-02).** After,
  so the copy is of a database this build can read back; before, so nothing is writing while the file
  is copied — a backup taken mid-transaction looks fine until the day it is needed. It swallows every
  failure (a shop that cannot take orders because it could not take a backup has been made worse by
  the feature meant to protect it), and the panel's "last backup" line is where a silently failing
  schedule becomes visible. It writes NO new format: a backup is the package `ExportDatabaseTo`
  already produces and a restore is `ImportDatabaseFrom`, which already backs up what it replaces.
- **A CSV writer for a multilingual application MUST emit a UTF-8 BOM (2026-08-02).** Excel on Windows
  reads a BOM-less file as the system ANSI codepage, so every Chinese, Japanese, French and Spanish
  name becomes mojibake — on the one machine the shop will actually open it on. Also neutralise a
  leading `=`, `+`, `-` or `@`: a spreadsheet treats such a cell as a FORMULA, which makes any stored
  text an injection vector the moment the file is opened.
- **An export must take what the LIST shows, which is why the filter had to become a model
  (2026-08-02).** The search text and the status filter were two view-model fields matched by two
  `if`s inside `RebuildOrdersView` — a private method the export could not call. `OrderQuery` is the
  one definition and `MainViewModel.FilteredOrders` is what the export reads. A file with more rows
  than the screen it came from is the version nobody re-checks.
- **Seeded demo data must carry OFFSETS, not dates (2026-08-02).** A file of absolute dates ages: a
  year after it ships every demo order is long collected, the pickup queue is empty, nothing is
  overdue and the settlement report has nothing in its period. `DemoOrderTemplate` stores
  `OrderDaysAgo` / `PickupDaysAfterOrder` and `DemoOrders.Seed` resolves them against the seeding day.
  Two rules ride along, and both are invisible until they are wrong:
  - **The set must include same-day records.** Month-to-date is the settlement report's default, so a
    set whose smallest offset is 1 produces an empty report on the 1st of every month — the shape that
    already shipped once. `democheck` asserts `Any(OrderDaysAgo == 0)` rather than "some order is in
    this month", which would pass on 27 days out of 28 while proving nothing.
  - **The demo tax rate is a DEMONSTRATION rate, not the preset's.** The shipped Canadian and US
    entries quote `standardRatePercent: 0` (sales tax is added at settlement), so a demo store seeded
    straight from them shows zero tax on every order, in the report and on every receipt.
    `DemoOrders.DemonstrationRatePercent` fills that gap only where the location quotes nothing.
- **A calculation that reads a process-wide setting cannot be run for a shop that is not open
  (2026-08-02).** `PaymentTaxRules.Active` is assigned when a shop is OPENED, because every order the
  app handles belongs to the open shop. Seeding a demo store from Store Management breaks that
  assumption — a different shop may be open, or none — so every `TotalAmount` came out taxed by the
  wrong shop's rules and disagreed with what the app recomputed on reload. `DemoOrders.Seed` sets the
  demo shop's rules active for the length of the seed and restores them in a `finally`. The
  alternative was a second copy of the tax arithmetic, which is exactly the thing this codebase keeps
  in one place. **Proved load-bearing:** removing the swap turns `democheck`'s "stored total matches
  the recomputed one" red.
- **A shortcut is a MODULE, not a `KeyDown` switch per window (2026-08-02).** `Controls/CopyPasteBinding`
  is an attached property taking an `ICopyPasteSurface`; a screen answers five questions and declares
  one line of markup. Four things it took a round trip each to get right:
  - **Attach to the LIST, never the window.** A window-level binding is reached from anywhere inside
    it and silently redefines Ctrl+C in the search box, the notes field and every editable combo.
    Attached to the list, those controls answer first because their own class input bindings are
    nearer the focused element.
  - **Install BOTH a `CommandBinding` and a `KeyBinding`.** The command binding executes and gates
    through `CanExecute`; the input binding guarantees the gesture is translated at that element
    rather than relying on the command's registered gestures being consulted up the route. Only one
    can win, so there is no double execution.
  - **A `RelativeSource AncestorType=Window` binding does not resolve until the window is SHOWN.**
    Asserting the attached property straight after the constructor reads as "the markup never set it".
    Show the window first — `storerender` does, and says so at the call site.
  - **Remove only what a previous assignment installed.** Re-assigning the surface must not stack a
    second pair of bindings, or one Ctrl+V pastes twice.
- **The clipboard KIND is where cross-context safety belongs (2026-08-02).** The orders list holds its
  selection under `Orders@{shop PublicId}`, so copying in one branch and pasting in another finds no
  match and Paste is simply disabled. Checked inside `CanPaste` instead, the copy would run against a
  shop-filtered context, find nothing and report "0 copied" — a silent no-op reads as a broken
  feature. Shops are held under a bare `"Shops"`: a shop belongs to the installation, so there is no
  context for one to go stale in.
- **Copy an aggregate by ID, and re-read (2026-08-02).** Both surfaces put ids on the clipboard, never
  the rows. A paste can arrive long after the copy, naming a record that has since been edited,
  paged away or deleted — re-reading duplicates what is there NOW and silently skips what is gone,
  where a snapshot would resurrect a deleted shop as a new one.
- **A "copy of" suffix is DATA, and its number belongs to the shop rather than to the language
  (2026-08-02).** `Store.Copy.Suffix` is punctuation as much as a word (zh writes `（复制）`, en needs a
  leading `&#32;` so a whitespace-trimming editor cannot eat it). The collision number is chosen once
  from every name of every shop and applied to all languages at once: picked per language, a shop
  whose English name collides and whose French one does not comes out "(copy)" in one and "(copy 2)"
  in the next. The batch adds its own new names to the taken set as it goes — two copies made in one
  click would otherwise both be "(copy)", the same defect batch Copy Order shipped with.
- **A harness runs against ITS OWN copy of the DLL (2026-08-02).** A scratchpad harness copies
  `CameywareOrder.dll` into its output at build time, so rebuilding the APPLICATION and re-running the
  harness exe exercises the previous build. A proof-of-failure ran green for exactly that reason
  before the harness was rebuilt — the "compiled and PASSED against stale code" trap, in a new
  disguise. Rebuild the harness after every application change.
- **A horizontal StackPanel measures its children against INFINITE width (2026-08-01).** So a star
  column inside one never grows, and a control set to stretch sits at its minimum forever. The
  settlement report's date range looked pinned because it was inside one; a `DockPanel` with the
  range as the filling child is what actually gives it the leftover width. Same measuring rule that
  made the Custom Service column need a Grid rather than a StackPanel.
- **A control in a WrapPanel needs margins in BOTH directions (2026-08-01).** A right margin alone
  spaces items across and lets the rows touch the moment they wrap — invisible until the data grows
  enough to wrap, which is why it shipped. Give the item equal margins and the container a matching
  negative one, so the trailing gap does not push the block past whatever it is aligned with.
- **Date boxes take a MinWidth, never a Width (2026-08-01).** The drop-down is floored at the box's
  width (`CalendarSizing`), so a box pinned narrower than the calendar needs leaves the calendar
  hanging off it — measured at 150 against 424. The floor lives in `ThemedDatePicker` so no call site
  has to remember it.
- **A second consumer of a money rule gets an ACCESSOR, never a copy (2026-08-01).** The settlement
  report needed "what did alterations take" — which meant knowing the money is `AlterationMoney`,
  that cleared is `AlterationBalanceCleared` passed through a private helper, and that the final
  method falls back to the deposit's. Three facts, and a report that copied them would have been free
  to disagree with the receipt. `Order.MoneyFor/ReceivedFor/OutstandingFor/MethodFor/SplitFor` select
  by `ServiceLine` instead, written as switches so a line added later fails to COMPILE rather than
  silently returning nothing.
- **A payment split says how to DIVIDE a stage, not what the stage is worth (2026-08-01).** Split
  lines are pre-tax amounts, so summing them to attribute money by method leaves the figures short of
  what was received. Apportion the stage's known received total across its lines by share, and give
  the last line the rounding remainder — which keeps cash + card + transfer exactly equal to the
  money received. That equality is the invariant a settlement sheet lives or dies on.
- **A report's period filter is a MODEL, not two DateTime fields on a window (2026-08-01).**
  `DateRange` is half-open (`Start` inclusive, `EndExclusive` not), which gets month boundaries right
  without anybody writing `AddMonths(1).AddDays(-1)` and meeting February; local, because "August" is
  the shop's August and `Contains` converts the stored UTC instant; and it knows its own KIND so
  previous/next can step by a month, a year or a custom span's own length.
- **Seeding demo history on the 1st of a month leaves a monthly report empty (2026-08-01).** Obvious
  in hindsight and not before: the seeder spread four months of orders backwards, the default period
  is month-to-date, and month-to-date was one day long. Demo data has to be seeded relative to the
  period the feature DEFAULTS to. Two related traps in the same pass: the shipped Canadian tax preset
  quotes 0% (sales tax is added at settlement there), so every tax figure was zero until the demo
  shop was given a real rate; and `CustomMadeServiceRecord.Subtotal` is COMPUTED from `Price`, so
  hand-built JSON carrying "Subtotal" deserialises to a record worth nothing — serialise the real
  type.
- **A warning colour has to be drawn ABOVE the selection highlight (2026-08-01).** The pickup tint
  went in under it, which looked right in the markup and was wrong on screen: the list opens with the
  FIRST row selected, the ordering puts the most overdue order first, and the opaque selection colour
  painted over the only warning visible. The tint now sits above the highlight and below the accent
  bar, and stays translucent so the two mix. Only rendering catches this class of bug — every
  assertion about the colour passed.
- **"Order by pickup date" is not the whole rule — FINISHED orders have to sink (2026-08-01).** A job
  collected last month carries last month's pickup date, which sorts it ahead of everything due this
  week. Seeding fifty demo orders showed eight completed and cancelled ones sitting above every
  overdue one. The list is a work QUEUE, so `IsPickedUp || IsRefunded` is the first sort key and the
  date is the second. Clicking the column header still sorts by the date alone — an explicit sort is
  a different question from the default view.
- **A date-state that drives colour is derived from `DateTime.Today`, so it moves with the clock
  rather than the data (2026-08-01).** `Order.PickupDue` is recomputed on every read and the list
  re-reads it when it reloads. Nothing persists "overdue", which means nothing can be stale — and
  also means a window left open overnight shows yesterday's colours until it is refreshed.
- **Required-but-not-a-TextBox goes in the SAME pass as the text fields (2026-08-01).** The pickup
  picker reuses the two-closure `RequiredTextField` the phone field introduced, so a form missing a
  customer name AND a pickup date reports both at once. "Blank" and "in the past" are deliberately
  separate checks with separate messages — one check reporting both would name whichever it hit
  first and leave the other for the next attempt.
- **The version lives in `Directory.Build.props` (2026-08-01).** `Version` / `FileVersion` move every
  release, `AssemblyVersion` only on a major (a patch must not break a binding reference), and
  `InformationalVersion` is the one allowed a suffix. Before this a built exe reported 1.0.0 for
  every release ever shipped. README's "Latest release" heading and this number name the same thing.
- **A language PREVIEW is a scope, not a global switch (2026-08-01).** Checking a translation used to
  mean switching the whole application into a language and back. `LocalizationScope` is the smaller
  unit: same table, same fallbacks, one panel's own language. It is a plain object with a
  parameterless constructor **on purpose**, so a panel declares it in its own `Resources` and every
  existing binding changes by one word — `Source={StaticResource Scope}` for
  `Source={x:Static loc:LocalizationService.Instance}`. Three traps, all paid for:
  - **A `Window`'s own properties are set before its `Resources` exist**, so `Title` cannot be bound
    to a scope declared there. It fails as a resource-not-found at parse time. Set the title in code.
  - **Rows built in code do not re-render from a binding refresh.** The scope raises `"Item[]"`,
    which reaches the markup; anything whose text was resolved in C# has to be rebuilt from
    `TextChanged`.
  - **The scope subscribes to the singleton**, so the singleton holds the panel alive. `Detach()` in
    `OnClosed`, same rule as `MainViewModel.Detach`.
- **A preview control must NOT render itself in the language being previewed (2026-08-01).** Preview
  Japanese with a picker that has itself turned Japanese and there is nothing left on screen the
  reader can use to get back. Generalised: the split is DISPLAY versus INSTRUCTION — what is being
  examined follows the preview, what the user must ACT on (the picker's label, confirmation dialogs,
  warnings) stays in the language they actually read. The terms panel follows the same line.
- **Take `ILocalizedText`, not `LocalizationService` (2026-08-01).** A helper that composes a
  localized string has no opinion on WHICH language; taking the service hard-wires "whatever the
  application is currently in" into it, and that is exactly the assumption a preview breaks.
  `MeasurementGenderPresentation.NameText` was the first to change; the service still implements the
  interface, so no call site had to.
- **Source folders were split three ways; NAMESPACES were not (2026-08-01).** `Views/`, `Models/` and
  `Services/` each hold `UserManagement/`, `StoreManagement/` and `Global/`, but everything under
  `Views/` is still `CameywareOrder.Views`. Moving the namespaces too would have touched every
  `using`, every `x:Class` and every `xmlns:` for no gain. What made the move safe was checking
  first that **nothing references a source PATH** — no pack URI, no `MergedDictionary`, no csproj
  item. The two `Themes/` dictionaries ARE referenced by absolute pack URI and did not move. One
  thing does need doing after such a move: **delete `obj/`**, or the generated partials at the old
  paths compile alongside the new ones.
- **A field that records a DATE must return the stored instant untouched when the day has not
  changed (2026-08-01).** `Order.ResolveOrderDate(picked, recorded)` compares DAYS and hands back
  `recorded` itself when they match — it does not re-derive midnight from the picked day. Two things
  depend on that and both are invisible until they break:
  - an order saved without touching the picker keeps the real TIME it was taken, so the field
    changes nothing for the normal case;
  - `UpdateExistingOrderAsync` decides "was this actually edited?" by asking EF whether any property
    `IsModified`. Rewriting the date to midnight on every save would make every re-save an edit and
    overwrite the record of who last really touched the order.
  A backdated day is stored as that day's LOCAL midnight converted to UTC, never
  `SpecifyKind(day, Utc)` — the naive version reads back as the day before everywhere east of
  Greenwich. `Order.OrderDateLocal` is the read side; the list and the detail panel bound
  `OrderDate` RAW and were already showing the wrong day near midnight before any of this.
- **DisplayDateEnd hides days; BlackoutDates refuses them (2026-08-01).** Both stop a future date
  being PICKED, and the difference only shows when you look: `DisplayDateEnd = Today` makes every
  later day cease to exist, so on the 1st of a month the drop-down is a calendar headed "August
  2026" containing one day. It also does **not** refuse a date TYPED into the box (measured), while a
  blackout does, because `DatePicker` asks the Calendar whether the parsed date is a valid selection.
  Blackouts it is — and the custom `CalendarDayButton` template has to draw the strike itself, since
  it replaced the stock one that drew it.
- **`CalendarSizing` sets a MinWidth, not a Width (2026-08-01).** The month grid inside a `Calendar`
  is content-sized and centred; it does not stretch to fill the panel. So the day cells decide how
  wide the grid needs to be, and a hard `Width` taken from a narrow box CLIPS columns off it. The
  order editor's picker is wide enough to hide that; Store Members' four are not.
- **A harness must not drive a Save that will be REFUSED (2026-08-01).** `AnnounceValidationFailure`
  raises a `MessageBox`, which blocks the thread until a human clicks it — a hung run, and a dialog
  on the user's screen. Drive `ValidateForSave` (the half that marks without announcing) by
  reflection instead. That split exists for this; `datecheck` also asserts the method is still there,
  so removing it fails a test rather than silently re-hanging the next harness.
- **Diff every COLUMN across a save, not just the one you changed (2026-08-01).** Asserting "an
  untouched save moves nothing" found two things that were not the order date: an order whose service
  category is "None" clears its section on the next save (the price box is ignored while the category
  switches the service off), and the legacy aggregate `Orders.FinalBalanceMethod` reads the raw final
  radio while the per-section column stores the RESOLVED method, so the two disagree until the first
  re-save reconciles them. Both are pre-existing and neither was visible from a passing test — the
  diff named them in one run where "LastModifiedDate moved" would have sent the next reader hunting.
- **A "batch" version of an action inherits every latent defect of the single one, at scale
  (2026-07-31).** Multi-select turned `CopyOrderAsync`'s hand-built
  `$"ORD-{DateTime.Now:yyyyMMdd-HHmmss}"` from a cosmetic wrong into a guaranteed one: every copy in
  a batch is composed from the same second, so all of them came out with the SAME order number.
  Before making an action repeat, read what it does once and ask which parts were only ever correct
  because a human could not do them twice quickly.
  - The fix was to route Copy through `OrderNumberFormatter.Reserve`, which it should always have
    used — the hand-built number also ignored the shop's configured prefix and mode entirely.
  - `Reserve` could not save it as written: it **returned early in Timestamp mode, ahead of the
    collision scan its own summary promises**. A method whose doc comment describes a guarantee one
    branch does not implement is worse than one carrying no comment — that branch is exactly where
    nobody looks. `ReserveTimestamp` now steps the number's second forward until it is free. Only
    the NUMBER's timestamp moves; the order keeps the date it was written.
  - **Reserve asks the DATABASE what is taken, and EF cannot see added-but-unsaved rows.** So the
    batch saves one copy at a time rather than batching one `SaveChanges` — batching would re-issue
    the same number to every copy for a second, unrelated reason.
- **`SelectedItems` is not a dependency property, so a multiple selection cannot be bound
  (2026-07-31).** `SelectedItem` can; `SelectedItems` cannot. The view pushes the selection into the
  view model from `SelectionChanged` (`MainViewModel.SetSelection`), one direction only, and the view
  model asks for a selection back through an event (`SelectionRequested`) rather than writing to the
  list — which is what stops a selection change re-entering through the event that reported it.
  - Consequence worth remembering: a view model holding a selection must **collapse it on every
    rebuild**. Ctrl+A means "this page", and paging happens in the view model, so a selection carried
    through a search, a sort or a page turn leaves Delete reaching rows no longer on screen.
- **`SelectionMode="Extended"`, never `Multiple` (2026-07-31).** Multiple makes every plain click a
  toggle, silently redefining the click the list has always had. Extended keeps plain click = one
  row, adds Ctrl+click and Shift+click, and is what `StoreManagementWindow` already uses.
  - Right-click must REPLACE a selection it lands outside of, not extend it: setting `IsSelected` on
    its own ADDS the row in Extended mode, so the context menu would act on one more record than the
    user pointed at. Inside an existing selection it leaves it alone — that is how the menu comes to
    act on the batch at all.
- **Ctrl+A on a ListBox is `OnKeyDown`, NOT a command — do not assert it through
  `ApplicationCommands.SelectAll` (2026-07-31).** The first version of `batchcheck` did exactly that
  and found `CanExecute` **false**, on our list and on a stock Extended `ListBox` alike, because
  `ListBox` never registers that command; it handles the gesture in `OnKeyDown`, gated on
  `CanSelectMultiple`. Asserting the wrong mechanism would have passed while proving nothing.
  - Synthesising a `KeyEventArgs` does not work either: `ListBox` reads `Keyboard.Modifiers`, which
    comes from real device state, so a fabricated event arrives with no Ctrl held. The honest test is
    real input — `keybd_event` after winning the foreground — and **fail rather than skip** when the
    foreground cannot be won, because the alternative is typing into somebody else's window.
    `SetForegroundWindow` plus a temporary `Topmost` is what stopped it flapping.
- **Render AFTER the animation, or the screenshot lies (2026-07-31).** The orders list fades its
  selection highlight over 0.30s. A render taken straight after a selection change caught it mid-fade
  and produced a "one row selected" screenshot in which all nine rows still looked selected — which
  reads as a real defect and is not one. Pumping the dispatcher is not enough; real time has to pass
  (`Settle(600)` in `batchcheck`). Anything with a transition has the same trap —
  `Animations/PanelTransition` runs 0.5s.
- **A harness must load the string table itself (2026-07-31).** `App.OnStartup` calls
  `LocalizationService.LoadFromDirectory`, which a harness never runs, so every lookup returns its
  own key and any assertion on localized text fails for a reason that has nothing to do with the code
  under test. `batchcheck` reported four such failures on its first run. Load it the way startup
  does: `LoadFromDirectory(SystemSettingsPaths.LanguagesDirectory, AppDefaults.Load().DefaultLanguageCode)`.
  - Supplying a harness's own `IServiceScopeFactory` to `MainViewModel` is the clean way to run
    against a throwaway SQLite file and never touch the user's database. Set the shop on
    `ShopContext` **before** any `AppDbContext` is constructed — the context captures the shop id in
    its constructor.
- **A per-shop setting needs a fallback that reproduces the OLD behaviour, or it is a
  migration (2026-07-28).** `Shop.InstalledLanguagesJson` decides which languages a branch runs in.
  Null — every existing shop — reads back through `ShopLanguages.Installed` as just the shop's
  `PreferredLanguageCode`, which is exactly one language and therefore exactly the behaviour those
  shops already had. Nothing changes until somebody installs a second. Had the fallback been "all
  shipped languages", every branch in the world would have gained a language toggle on upgrade,
  from a change nobody asked them about.
  - The other end of the same rule: a shop with **neither** an installed set nor a preference has
    restricted nothing, so it gets everything. Both answers are the shop's own statement read as
    literally as possible, which is what makes them defensible without a table of special cases.
  - Resolve such a set against what actually SHIPS, in the shipped order — a stored code whose
    `*.lang.xml` was removed must be dropped, or the screen renders every key as its own name.
- **`ShopLanguages` is a product of a CAPABILITY and a SHOP, so it lives in neither (2026-07-28).**
  "Which languages may this session pick from" = administrator ? all shipped : the shop's installed
  set. Putting it on `AuthenticationService` would have had it reaching into `ShopContext` and vice
  versa. Four surfaces consume it — the toolbar toggle, the shop editor, the measurement print
  dialog and the PDF download panel — which is the count at which a copied rule reliably drifts.
  - **Rename a capability when its meaning narrows.** `CanChooseLanguage` became
    `CanChooseAnyLanguage`: under the old name, `false` read as "no language toggle", which stopped
    being true once a shop could install several. The rename forced both call sites to be re-read,
    which is the whole point.
  - **Enforce a pairing by what the control CONTAINS, not by validating it afterwards.** The
    preferred-language picker lists only the languages ticked as installed, so "a shop opens in a
    language it runs in" cannot be violated in the first place — no error message, no rule to keep
    in step. Where the two CAN still disagree (rows written before the setting existed),
    `ShopLanguages.PreferredCode` resolves it rather than each caller reading the raw field.
- **Text written from code does not follow a language switch (2026-07-28).** `MainWindow`'s greeting
  had been going stale since it was added: it is assigned in `RefreshSignedInUser`, and
  `OnLanguageChangedGlobally` re-bound the DataContext but never re-ran it. Anything set as
  `Control.Text = ...` rather than `{Binding}` needs an explicit call from that handler. Worth a
  sweep whenever a code-written label is added next to a bound one — on screen they look identical
  until the language changes.
- **Reaching a control as "the first X in the window" is a latent bug (2026-07-28).** `pagingcheck`
  found the orders search box that way, and it silently became a ComboBox's internal
  `PART_EditableTextBox` the moment the language picker stopped being collapsed for a
  nobody-signed-in harness — the assertion then failed on focus, which reads as a paging regression.
  Give a control an `x:Name` when a test needs to address it; a themed ComboBox/DatePicker carries
  inner TextBoxes that will happily answer to a type-based search.
- **A records list must not let one cell decide a row's height (2026-07-28).** The orders list is
  read by scanning DOWN a column, so a row that is taller than its neighbours breaks the only thing
  the layout is for. Rules now enforced by `Themes/AppTheme.xaml`'s `ListCellText` (`NoWrap` +
  `CharacterEllipsis`, and deliberately no size or colour — the row takes those from the font-size
  slider and the gray-out trigger):
  - **`DisplayMemberBinding` generates a bare `TextBlock` that cannot be styled.** Its content is
    clipped mid-glyph on overflow — no ellipsis, no tooltip, no way to read the rest. Use a
    `CellTemplate` for any column whose value can be long.
  - **A horizontal `StackPanel` defeats `TextTrimming` completely.** It measures children with
    infinite width, so a child never learns it overflowed and the ellipsis never appears. Use a
    `Grid` with an `Auto` + `*` pair; the star column is what constrains the text.
  - An ellipsis hides data, so pair it with a `ToolTip` carrying the full value.
  - With nothing wrapping, `HorizontalScrollBarVisibility` must be `Auto`, not `Disabled` — a
    window too narrow for the columns otherwise leaves the rightmost ones unreachable.
  - **Assert row height by MEASURING real containers, at both ends of the font-size slider.**
    Wrapping bites hardest when the text is large, and the defect only shows on rows whose values
    happen to be long — so the harness seeds a value long enough to overflow and asserts that at
    least one cell really is truncated, or the ellipsis checks pass on a list where nothing ever
    was. `scratchpad/rowcheck` is the worked example.
- **Splitting a stored field needs the OLD property kept, or the migration eats the data
  (2026-07-28).** `DisplayName` became `FirstName` + `LastName`. Deleting the property outright
  would make `System.Text.Json` discard the value on exactly the load that was supposed to migrate
  it — every person silently loses their name, and the file is rewritten without it before anybody
  notices. Keep it as a `[JsonPropertyName("DisplayName")] LegacyDisplayName`, read it once, clear
  it, and write it back only `WhenWritingNull`. Same shape as `LegacyRole` / `LegacyAssignments`.
  - **A name split is a guess about a real person, so guess conservatively.** No whitespace →
    the WHOLE value is the first name. A Chinese name is family-name-first with no separator, so a
    positional split would greet 林艳 as "林" — her surname alone. With whitespace, split at the
    LAST space ("Mary Jane Watson" → "Mary Jane" + "Watson"). Both are lossless: re-joining gives
    the original back, which is the property to assert.
  - Guard against re-splitting: a record already carrying either half is left alone, or a later
    load could clobber an edited name from a stale single field.
- **Renaming a login touches more than the record (2026-07-28).** Three things bite:
  - **`CredentialFile.ProvisionedAccounts` must be LEFT ALONE.** It records which SEED NAMES this
    installation has created, and `ProvisionSeedAccounts` looks each seed name up in it directly —
    so the old name staying put is exactly what stops a re-seed. Rename the entry from `staff` to
    `sam` and the next load finds `staff` both absent and unlisted, and creates a brand-new `staff`
    **with a known password** beside the renamed one.
    - Got backwards on the first attempt, with a confident comment asserting the opposite, and the
      harness agreed because it only ever renamed an account that was never SEEDED. **A rename test
      that does not rename a seeded account proves nothing about seeding.**
  - `RefreshCurrentUser` identifies the session BY USER NAME. After a rename the record no longer
    matches itself, so the refresh silently no-ops and the session keeps a login that no longer
    exists. Decide "is this the signed-in account" BEFORE the rename, and adopt the record after.
  - The administrator's login can never change — a PRODUCT rule (that account must stay identifiable
    and can never be deleted or demoted), refused in the service. Independently, `ProvisionSeedAccounts`
    asks "is there an ADMINISTRATOR" by flag rather than "is there an account called admin", so the
    "exactly one administrator" invariant holds structurally rather than resting on that guard.
  - Memberships need no attention — they key on `Shop.PublicId`, not on the login. That is the
    payoff of the decision recorded above.
- **Handing out a SESSION is gated in the service, not just the UI (2026-07-28).** `SignInAs` lets an
  administrator take over another account. Every roster edit beside it is gated by its caller —
  "callers gate, this layer only stores" — but that convention is about writing DATA. A method that
  changes who the application thinks you are must refuse in the service too, where a future call
  site cannot skip it.
  - Clear the bound shop on the switch. Capabilities resolve against `_activeShopPublicId`, and the
    new user may hold no role in the shop the ADMINISTRATOR had open.
  - Refuse an account that is delisted everywhere. Sign-in already refuses those, so becoming one
    only spends the administrator's own session to reach "no shop is available" and then the login
    screen — a trap, for information the roster already shows.
  - The screen that offers it must only REPORT the choice. Swapping the session from inside a dialog
    pulls that dialog's own ground out from under it, and the main window has to come down first
    anyway (a capability swap under a live window leaves the previous person's chrome on screen).
- **"Add an SVG icon" in WPF means `Path` geometry (2026-07-28).** `Path.Data` IS SVG path syntax,
  rendered natively and crisp at every DPI. An actual `.svg` file cannot be shown at runtime — no
  rasterizer is installed on this machine, which is why `Assets/ICONS/app-icon.svg` exists only as
  the design source for the `.ico`. Follow the Store Members button in `MainWindow`: a `Canvas` with
  `Ellipse` + `Path` children, stroked from theme brushes so the icon follows the palette.
- **Disable, do not make read-only, when a field cannot be edited (2026-07-28).** A read-only
  `TextBox` looks exactly like an editable one and silently swallows typing; the report that came
  back was "the system blocked me to update user name" with no idea why. `IsEnabled = false` greys
  it, and a line underneath says which rule applies.
- **Two buttons with the same label saving different subsets is a data-loss bug (2026-07-28).** The
  user-management pane had a Save on the profile card and a Save in the footer, both reading "Save
  Changes"; the footer's applied only the password and the shop roles, so a name or login typed into
  the card was discarded by the reload that followed — under a "changes were saved" message. One
  Save per screen, applying everything on it. Order matters when one part renames the record: apply
  the profile FIRST, then use the new login for everything after.
  - The check that would have caught it drives the REAL handler and reads the value back from the
    service. A check that called `UpdateAccountProfile` itself would have passed throughout.
- **A native MessageBox cannot be automated, and the seams that would let it be are worse
  (2026-07-28).** A XAML window cannot be subclassed to override a confirmation —
  `InitializeComponent` calls `Application.LoadComponent(this, uri)`, which resolves the resource by
  EXACT type and throws for a derived class. An injectable delegate would be a test hook in shipping
  code. So: drive the handler for every path EXCEPT the confirmed one, cover that one against the
  service, and write the coverage boundary at the call site so the gap is visible rather than
  assumed away.
  - Corollary worth remembering: a harness that pops a modal blocks until a HUMAN clicks it — which
    is how one appeared on the user's screen mid-run. Check for `MessageBox` on any path a harness
    drives.
- **A harness must establish the ACCOUNTS it reads, and "deleted" is a legitimate state
  (2026-07-28).** `authcheck` asserted that the seeded `test1` / `test2` accounts exist with no
  memberships. They had been deleted in the running application, and `ProvisionedAccounts` makes
  that deletion **permanent by design** — so they never come back and five checks failed over a
  correct user action. It now calls `CreateAccount` for each fixture account first (success or
  `UserNameTaken`, either way it exists) and still restores the file byte-for-byte, so the user's
  deletion stands. This is the FOURTH way this one harness has rotted against live user data —
  path, shop list, passwords, and now account existence. The rule generalises: a harness reading
  `credentials.json` or `orders.db` must create everything it asserts on, because every one of
  those things is something a person is entitled to change.
- **A static singleton latches its file on FIRST TOUCH, which reflection can trigger early
  (2026-07-28).** `AuthenticationService.Instance` reads `credentials.json` exactly once, in the
  type initializer — and `Activator.CreateInstance(typeof(AuthenticationService), …)`, which
  `namecheck`/`authcheck` use to build throwaway instances, runs that initializer. So a harness that
  exercises the service first and opens a window second finds `Instance` holding the FIRST fixture,
  and the window then rewrites the file from that stale in-memory copy the moment it saves. Order
  the sections so the singleton is born holding the fixture the windows need, and say so at the call
  site — it looks like arbitrary ordering otherwise.
- **Watch a harness's assertion COUNT, not just its pass/fail (2026-07-28).** `menucheck` reported
  32 on some runs and 35 on others, always green: its `ContextMenu` closes when the window loses
  foreground, and the three highlight assertions were quietly not running. A skipped assertion
  reports identically to a passing one. Where a harness can skip work, either fail on the skip or
  assert a minimum check count — a run that quietly does less is not a passing run. (Left as an
  observation, not yet fixed.)
- **A harness must establish the SCHEMA it reads, not just the rows (2026-07-28).** `headercheck`
  shares a fixture database with `migcheck`, which rewinds it. Every column added to `Shops`
  afterwards made headercheck fail with "no such column" — but only when it ran BEFORE migcheck, an
  ordering dependency nobody would think to look for. It now calls the application's own
  `EnsureShopSchemaAsync` first. Same rule as seeding rows: if a harness depends on a state, it
  creates that state.
- **A harness can be red for weeks if the suite is run selectively (2026-07-28).** `uicheck`'s menu
  check had been throwing a `NullReferenceException` since the menus were themed — a MenuItem
  inside a popup has no `Template` until it is realized, so `submenu.Template.FindName(...)` was a
  null dereference that reads as "the menu is broken". `ApplyTemplate()` fixes it. Two consequences
  worth keeping: run the WHOLE suite before claiming it is green (`scratchpad/run-suite.ps1` does,
  and handles the three different tally formats the harnesses print), and when a check's NAME
  describes a reverted feature — this one still said "right-aligned with left carets" — rename it,
  because the next person reads the name as the specification.
  - Proving a failure is pre-existing is cheap and worth doing before touching anything: build a
    throwaway copy of the harness pointed at an older `CameywareOrder.dll` and run it. It settles
    "did I break this" in one step instead of by reasoning.
- **All harnesses now compile against `bin/Debug/net8.0-windows` (2026-07-28).** Fourteen of them
  pointed at `scratchpad/navswap/bin`, a build from an earlier session — so they compiled and
  PASSED against stale code, the worst possible failure mode. Repointed. The reason the split
  existed (the app locks its own output while running, so builds get redirected with
  `-p:OutputPath`) is real but rare: kill the app and build normally instead.
- **One theme for the whole app: `Themes/AppTheme.xaml`, merged in `App.xaml` (2026-07-27).** Palette
  brushes (`PrimaryBrush` #4F46E5, `AccentBrush` #7C3AED, `HeaderGradientBrush`, the neutral ramp, the
  danger/success/warning pairs) plus implicit styles for Button / TextBox / PasswordBox / ComboBoxItem /
  DatePicker / CheckBox / RadioButton, and keyed `CardBorder` / `CardHeading` / `FieldLabel` /
  `SectionHeading` / `RosterCardContainer`. A window needing a variant should base its style on one of
  these and never restate a colour.
  - **Colours that ENCODE MEANING stay literal at their use sites** — balance status (green / light
    green / orange / red), the refunded-order strike. A theme sweep must never quietly change what a
    colour tells the user.
  - **A custom `ComboBox` ControlTemplate must reimplement TWO things, and both are easy to miss**
    (each cost a round trip here before the template finally worked):
    1. **`DisplayMemberPath` is resolved by a template SELECTOR, not by `SelectionBoxItemTemplate`.**
       `ItemsControl` installs an internal selector into `ItemTemplateSelector`, so the face needs
       `ContentTemplateSelector="{Binding ItemTemplateSelector, RelativeSource={RelativeSource
       AncestorType=ComboBox}}"` on top of the Content/ContentTemplate bindings. Without it the face
       falls back to `ToString()` — `LanguageOption { Code = …, Name = 简体中文 }` on screen.
    2. **`IsEditable` needs a `PART_EditableTextBox`** plus a trigger that swaps it for the face; the
       branding editor's font-size box is editable and silently stops accepting input otherwise.
    Bind with `RelativeSource`, never `TemplateBinding`: a TemplateBinding re-resolves against the
    NEAREST templated parent, which is the wrong element once the face sits inside the ToggleButton's
    own template.
  - **A localized `DatePicker` needs two separate fixes.** The watermark ("Select a date") comes from
    PresentationFramework's own resources and ignores the app's string table, so `DatePickerTextBox` is
    re-templated with a `Common.SelectDate` watermark; the calendar's month/day names come from
    `FrameworkElement.Language`, which each window carrying a picker sets from the current UI language
    (it is inherited, so setting it on the Window is enough).
  - Currency Setup is gone from Local Configuration — the currency is a property of a shop and is edited in Shop Settings.
    `CurrencySettingWindow` was deleted; `Toolbar.CurrencySetting` is still LIVE because the
    global-settings package description names it.
- **Put a menu where its drop-down can open, rather than mirroring the drop-down (2026-07-27).** A
  menu at the extreme right of a bar fights the window edge, because a drop-down opens down-and-LEFT
  from its item. A mirrored `MenuItem` template (labels right-aligned, caret pointing left, submenus
  `Placement="Left"`) was built for exactly this and then **reverted — it looked wrong.** Moving
  Local Configuration one slot left, so Store Members sits to its right, solved it with no template at all. Reach for
  ordering before reaching for a mirrored control.
  - If a mirrored menu is ever genuinely needed: it must be opt-in per menu (the orders row's context
    menu opens at the pointer and must stay normal), and it propagates with
    `ItemContainerStyle="{DynamicResource ...}"` naming itself — a StaticResource cannot refer to the
    style being declared, and without it only the first level of items mirrors.
  - **Screenshotting a menu:** a `Popup` lives in its own window and never appears in the parent
    window's `RenderTargetBitmap`. Render `popup.Child` instead — it is an ordinary visual.
- **An explicit `Style` REPLACES the implicit one — always `BasedOn` (2026-07-27).** A keyed style
  applied to a control with no `BasedOn` opts that control out of the theme completely, silently.
  That is how the login screen's two boxes ended up as the only unthemed inputs in the app
  (`FieldInputStyle`, TargetType=Control), and `ShopSetupWindow` had the same fault twice. When
  adding any keyed input/button style, base it on the themed one.
  - **This is the most common defect in this codebase — three separate occurrences so far.** The
    third was the orders right-click menu (`OrderContextMenuStyle`, `OrderMenuItemStyle`), and it
    cost more than colour: replacing the implicit `MenuItem` style threw away `ThemedMenuItem`'s
    whole ControlTemplate, and replacing the implicit `ContextMenu` style dropped
    `Grid.IsSharedSizeScope`, which is what lines up the icon gutter. **When a control looks
    off-theme, grep for a keyed style targeting its type before suspecting anything else.**
  - Often the right fix is to DELETE the local style, not to add `BasedOn` to it — if every setter
    it carries is already in the theme, keeping it only leaves the trap armed for the next edit.
- **Menus, popups and the theme (2026-07-27).**
  - A `Separator` inside a menu never receives the implicit `Style TargetType="Separator"`.
    `MenuItem`'s container preparation calls `SetResourceReference(StyleProperty,
    MenuItem.SeparatorStyleKey)` on it, and a **resource reference is a LOCAL value**, which
    outranks every implicit style. Style it with `x:Key="{x:Static MenuItem.SeparatorStyleKey}"`.
  - `ContextMenu` cannot be themed by setters alone: the stock template is a square-cornered Border
    behind a legacy offset-rectangle shadow. Write a `Template`. Its popup sets
    `AllowsTransparency = true` unconditionally in `ContextMenu.HookupParentPopup()`, so rounded
    corners and a real `DropShadowEffect` do composite — `HasDropShadow="False"` does not cost you
    that, it only switches off the stock shadow so the two do not stack.
  - A context menu opens AT the cursor, so shadow room in the template's `Margin` must be
    asymmetric — pad right/bottom, leave top/left near zero, or the menu drifts off the pointer.
  - **Style triggers outrank template triggers.** That is how `DangerMenuItem` keeps its red
    `Foreground` through `ThemedMenuItem`'s own `IsHighlighted` setter without copying the template.
    A style trigger still cannot reach a `TargetName` part, so part-level colours stay in the template.
- **The DatePicker's calendar needs THREE separate hook-ups, and every miss is silent (2026-07-27).**
  1. `DatePicker` BINDS its Calendar's `Style` to `DatePicker.CalendarStyle`, and a bound null Style
     **suppresses implicit-style lookup** — so an implicit `Style TargetType="Calendar"` never
     applies. It must be named: `<Setter Property="CalendarStyle" Value="{DynamicResource ...}"/>`.
  2. The day buttons likewise come from `Calendar.CalendarDayButtonStyle`, so the themed day-button
     style has to be set there too.
  3. WIDTH cannot be bound at all: the Calendar is created in code inside a `Popup` — a separate
     visual tree — so `RelativeSource AncestorType=DatePicker` finds nothing and reports NO error.
     `Controls/CalendarSizing.cs` sets it on Loaded/SizeChanged, before the first open.
  - None of these produce a binding error, so screenshots alone will mislead you. Assert the state
    (`calendar.CalendarDayButtonStyle is null`, `day.Template.FindName("Bd", day)`) in the harness.
- **Adding a column to Shops touches TWO lists (2026-07-27).** `EnsureShopSchemaAsync`'s
  `CREATE TABLE IF NOT EXISTS` serves fresh installs only — an existing database already has the
  table, so the guard does nothing there and every later column needs its own entry in
  `ShopColumnMigrations`. Do one and not the other and it works on exactly one kind of install,
  which is the kind you happen to be testing on. Same split applies to `OrderColumnMigrations`.
  - The new column should be **nullable** unless there is a real reason not to. SQLite's
    `ALTER TABLE ADD COLUMN` demands a DEFAULT for a NOT NULL column, and this codebase cannot write
    a `'{}'` default at all: `ExecuteSqlRawAsync` treats the SQL as a composite format string, so the
    braces throw `FormatException` before a statement runs. `PaymentTaxRulesJson` is the precedent.
  - Verify against a **copy of the real database**, and call the real private method by reflection
    rather than re-typing the DDL — a copied SQL string passes while the shipping code is wrong.
    Assert the second run is a no-op: startup repeats the migration on every launch, so a
    duplicate-column error would brick the app after one restart. `scratchpad/migcheck` is the
    worked example.
- **Per-language vs single-valued shop fields (2026-07-27).** The rule this codebase already
  follows: **prose is per language, identifiers are not.** Shop name and address are prose — they
  are printed on receipts and a zh-CN reader should not get the English wording — so both are
  language-keyed JSON (`NamesJson`, `AddressesJson`). Phone, email, website and
  `ReceiptBrandingSettings.TaxRegistrationNumber` are identifiers, identical whoever reads them;
  only their labels translate. Ask which kind a new field is before adding it.
- **Adding a language: what actually breaks (2026-07-28).** fr-FR was added as a third language and
  these are the things that were NOT obvious:
  - **A translation identical to English is invisible.** `Paging.Summary` came out word-for-word the
    same, which is indistinguishable from a missing key falling back. Assert that each key DIFFERS
    between languages, with an allow-list for values that are legitimately shared (currency codes,
    "cm", the qipao).
  - **French runs ~25% longer than English.** Fixed-width GridView columns sized against English or
    Chinese truncate their headers. Size such columns for the LONGEST language, and where that would
    disturb a width the user chose deliberately, shorten the TRANSLATION instead.
  - **Look for an existing symbol before drawing one.** The gender picker needed ♂/♀ marks; the
    measurement-terms list had been badging rows with those exact characters all along. A second,
    hand-drawn version would have been a definition to keep in step for no gain.
    `Views/MeasurementGenderPresentation` is now the single table both screens read, the way
    `UserPresentation` already served role names. Where a third symbol is needed and no safe glyph
    exists (⚥ / U+26A5 is not reliably in a UI font), COMBINE the ones that are proven — "♂♀" says
    "both" using only glyphs already drawing on screen here.
  - **A fixed `Width` on a glyph clips it, and the clipping looks like a font problem.** "♂♀" needs
    38.4px; at `Width="26"` the second mark was cut and read as a missing-glyph box. Use
    `Width="Auto"` with a `SharedSizeGroup` when several rows must align — it measures the widest
    instead of guessing, and `Grid.IsSharedSizeScope` on the ComboBox covers the closed face as well
    as the drop-down list.
  - **Fixed `Width` on a button is the same bug in miniature.** `Width="90"` rendered the French
    "Enregistrer" as "Enregistre". Use `MinWidth` + `Padding`: the buttons still look matched in a
    short language and either can grow. Assert it — measure each button's label against its
    `ActualWidth` — rather than trusting a screenshot.
  - **Radio groups do not survive translation.** A row of radios needs the width of EVERY label at
    once; a drop-down needs only the widest, and only while open. Measured in a 420px dialog: the
    three gender options needed ~291px in Chinese (fine), ~429px in English and ~463px in French
    (both overflow). Radios are for two or three genuinely short, stable options — in a bilingual+
    application that is rarer than it looks. Put the label ABOVE such a control, not beside it, or
    the width problem comes straight back.
  - **A stray `{1}` in a translation is a runtime crash**, not a cosmetic issue — `string.Format`
    throws `FormatException` when the string references more arguments than the call site passes.
    Assert placeholder sets match across every language.
  - Adding one really is just dropping a file into `Settings/System/Languages` — no .cs, no .csproj,
    no registry. That is the whole return on the Phase 1 split.
- **Deleting an "unused" language key is dangerous (2026-07-28).** ~34 of 500 keys are never written
  literally anywhere: they are composed at runtime as `$"Measure.Term.{id}"`,
  `$"PaymentMethod.{method}"`, `$"ServiceType.{type}"` and so on. Deleting one is SILENT — the lookup
  returns the key itself and the screen reads "Measure.Term.waist". A key is unused only if its full
  name is absent from the source AND its prefix is not one the code interpolates. `formatcheck`
  enforces this; only 3 keys were genuinely dead.
- **Sonar S2325 "make static" on an event handler: check how it is WIRED (2026-07-28).** A handler
  named in XAML cannot be static — the generated InitializeComponent emits `this.Handler`, which does
  not compile against a static method. A handler attached only from code (`+=`,
  `DataObject.AddPastingHandler`) can be. Of 7 findings, 5 were the former (suppressed with the
  reason) and 2 the latter (fixed) — and fixing those cascaded a third onto their caller.
- **Sonar S125 on a comment is usually prose that parses as code (2026-07-28).** All three instances
  were explanatory comments containing a trailing `;` or a `Type.Method` reference. Reword them;
  do not suppress, and do not delete the comment.
- **Language-dependent PUNCTUATION is data, not code (2026-07-28).** `Format.ListSeparator` and
  `Format.BulletSeparator` live in the language table. They replaced
  `code.StartsWith("zh") ? "、" : ", "`, which had been copy-pasted into FIVE files — so a new
  language silently got English punctuation in all five, with nothing to catch it.
  - Use `LocalizationService.JoinList(...)` / `JoinFragments(...)`. The API is deliberately a JOIN
    rather than a separator property: handing out the separator is exactly what let five private
    copies of the rule accumulate.
  - Spaces in a separator are written `&#32;` in the XML. The trailing space in `", "` IS the
    format, and a whitespace-trimming editor would silently produce `Jacket,Shirt`.
  - **Not everything belongs in the language file.** The short language name for an export filename
    (`Measurements_zh.pdf`) is the BCP-47 primary subtag — the same mechanical rule for every
    language, so it is DERIVED, not listed. Data that can be derived should not be maintained by
    hand. Likewise currency symbols: `CurrencyType` is an enum persisted as integers, so a currency
    needs a code change regardless and a JSON file would only add a second place to keep in sync.
  - A silent wrong default on a MONEY field is not cosmetic. An unknown currency renders `¤`, never
    `$` — the fallback must not state something false about an amount.
- **Three messages are unlocalized ON PURPOSE — do not "fix" them (2026-07-28).** Each carries a
  comment saying so at the site:
  - `App.OnStartup`'s catch-all MessageBox. It wraps the whole of startup, and loading the language
    table is part of startup — a localized message there could depend on exactly what failed.
  - `LocalDataFolderMigration`'s failure. It runs BEFORE the table is loaded, deliberately, because
    it must move the data folder before anything resolves a storage path.
  - By contrast the sign-out failure in `MainWindow` IS localized (`SignOut.Failed`), because by
    that point the table is loaded. The rule is "localize where the table is available", not
    "localize everything".
  - Also not translated, and correctly so: the `B` / `I` / `U` formatting-button faces, alignment
    glyphs, `×`, `—`. Typographic convention, not prose.
- **A field that is only populated "sometimes" is not a field you can read directly (2026-07-28).**
  `MeasurementValue` holds `Cm` and `In`, but `In` is written only when the editor's unit toggle is
  flipped while that value is on screen. The print path read `In` and skipped a blank — measured on
  the live database, 768 of 768 values had cm and 39 had inch, so printing in inches dropped 95% of
  rows and produced an entirely blank sheet for any order never toggled. Read such a pair through
  `MeasurementUnits.Resolve`, which converts from whichever unit was filled in.
  - Generally: before reading one of two parallel fields, ask what actually writes it. "Both are
    kept in sync" was true of the editor's in-memory cache and false of everything persisted.
  - The conversion lives in ONE place (`Models/MeasurementUnits`) because the editor, the printed
    sheet and the PDF must produce the same figure. They had separate copies, which is exactly how
    the print path came to disagree with the screen.
  - A measurement may carry a trailing `+`/`-` — a tailor's "runs over/under" note. Convert the
    digits and carry the mark through; dropping it silently changes what the measurement means.
- **A stored id is a compatibility surface — never rename one (2026-07-28).** The ready-made product
  ids (`Jackets`, `TiesBowtie`, …) are written into `OrderItem.ProductName` on every order ever
  saved AND are the suffix of the `ClothingItem.<id>` string-table keys. Renaming one silently
  orphans historical orders, which then print the raw id. Same for measurement term / garment ids.
  Add freely; never rename. `ProductCatalogDefaults` says so at the declaration, and `catalogcheck`
  asserts the five have not changed.
  - Corollary: a per-shop catalogue must keep resolving an id it no longer contains — a shop may
    delete a category it stopped selling, and its old orders still have to print. `ResolveName` goes
    id → the shop's own names → the string table → the raw id.
  - Predefined entries take their name from the STRING TABLE, not from stored text, so they are
    automatically translated into a language added later. Only user-added ones carry their own names.
- **A test must not pin a setting the user owns (2026-07-28).** Three formatcheck assertions
  hardcoded `defaultLanguage == "zh-CN"`; the user changed it to `en-US` and the suite reported their
  configuration change as three failures. `Settings/` is untracked, so git showed no diff to explain
  it either. Assert that a setting is HONOURED (the loader used what the file says, and the file
  names a language that ships), never that it holds a particular value.
- **Shipped configuration vs per-installation state (2026-07-28).** Two different things that both
  sound like "settings", and conflating them is how a user's data gets overwritten by an upgrade:
  - `Settings/System/**` next to the executable — language tables, `app-defaults.json`. Read-only,
    versioned in git, REPLACED wholesale by an upgrade. Located via `SystemSettingsPaths`.
  - `%LOCALAPPDATA%\CameywareOrder` — credentials, the database, branding, measurement terms.
    Read-write, never in git, must SURVIVE an upgrade and therefore needs migration code. Located
    via `UserDataPaths` — never re-derive the folder name, it was duplicated in six files once.
  - Ask which one a new file is before choosing where it goes.
- **Migrating the user's data folder (2026-07-28).** Rules that came out of doing it:
  - **Some layout is a data INTERCHANGE format, not just folders.** `Documents/` and `orders.db`
    cannot move: export packages store entry paths relative to the data root, so those names are
    baked into every zip a user already holds. Check what serialises a path before relocating it.
  - **Migrate lazily and FALL BACK.** `ResolveConfigFile` moves a file on first access and returns
    the OLD path if the move fails. Being unable to tidy up must never make credentials.json
    unreadable — a cosmetic folder change that can lock someone out of their own application is not
    worth shipping at any level of confidence.
  - **A sweep may move, never delete.** Deleting is a separate, explicitly-configured step
    (`backupRetentionCount` in app-defaults.json), applied only AFTER a new backup supersedes an old
    one — never on startup.
  - **Order backups by write time, not by name.** One real backup is `orders.db.bak-preShopRules`;
    name parsing has to guess at a suffix that is not a date, and guessing deletes the wrong file.
  - **Take the root as a PARAMETER so the migration is testable.** Every operation has an overload
    accepting the data root, so `userdatacheck` exercises it against throwaway folders. A migration
    that can only ever be run against the machine it must not break cannot be verified at all — and
    the alternative, a test-only seam on the class that locates credentials, is worse.
- **Splitting a translation table needs a parity check, or it is a downgrade (2026-07-28).** While
  every language lived in one file a missing key was a one-line grep; once each language is its own
  document the gap is invisible, because `TryGetText` quietly falls back to the default language and
  the screen looks fine in testing. `LocalizationService.KeyGaps` computes it. Reported, not thrown:
  a translation gap is a defect, not a reason to refuse to start in front of a user.
  - Also explicitly ORDER the discovered languages. File-system order is not the old file's order —
    `en-US.lang.xml` sorts before `zh-CN.lang.xml`, so the picker silently reshuffles.
  - Also REFUSE a duplicate language code. Copying `en-US.lang.xml` to `fr-FR.lang.xml` and
    forgetting the `code` attribute inside is the likeliest way to add a language, and letting the
    second win means the new language silently replacing the one it was copied from.
  - Split such a file with **byte-level tooling, never `XDocument.Save`** — it rewrites `&#32;`
    character references into literal spaces, which is exactly the fragility they exist to prevent.
- **`System.Text.Json` matches property names CASE-SENSITIVELY by default (2026-07-28).** A
  hand-written `"defaultLanguage"` does not bind to `DefaultLanguage`; the value comes back null and
  the code silently uses its fallback. Set `PropertyNameCaseInsensitive = true` for any file a human
  edits. This shipped undetected because the test fixture named the same language as the fallback —
  **a fixture whose expected value equals the fallback proves nothing.** Pick one it cannot produce.
- **Harnesses must seed or rewind their own fixture (2026-07-28).** They share one database copy, and
  a harness that WRITES (shopcheck drives the real Save handler; migcheck migrates the schema)
  destroys the precondition of its own next run — and of any other harness reading the same rows.
  The failure looks exactly like a regression and costs a round of diagnosis every time. Seed what
  you assert at the start of the run; for schema, rewind (`ALTER TABLE … DROP COLUMN`) rather than
  re-copying, so it still works once no pre-migration database exists anywhere.
- **Grep the source tree in a test to stop a pattern coming back (2026-07-28).** `formatcheck`
  asserts no `.cs` file contains `StartsWith("zh"` or a hard-coded separator literal. This is how
  the FIFTH copy of the language-sniffing rule was found, in
  `CustomMadeServiceWindow.ShortLanguageName` — it had no CJK character on the line, so the manual
  CJK grep that found the other four missed it entirely. Cheap, and it catches what review does not.
- **The login screen must not name accounts (2026-07-28).** `OnSignInClick` deliberately gives ONE
  message for an unknown user name and a wrong password, so the dialog cannot be used to discover
  account names. Pre-filling the box with `admin` handed that away before the first keystroke — and
  `admin` is the account that can never be deleted, demoted or locked out. Nothing on this screen
  may name an account. Consequence to keep in mind: a fresh install now gives no on-screen hint of
  the initial account, so it has to be communicated out of band.
- **MainWindow can be opened by a harness with NOBODY signed in (2026-07-28).** With no current user
  `IsAdministrator` is false, every capability gates closed and `RefreshSignedInUser` handles the
  null, so the window constructs — the chrome is just fully hidden. That is how to test anything
  role-INDEPENDENT (the header, the records panel) without going near credentials.json. The
  `AuthenticationService` singleton still READS the file when first touched, so hash it before and
  after and fail the run if it moved. `scratchpad/headercheck` is the worked example.
- **A property on a notifying singleton needs every notify SITE updated (2026-07-28).** `ShopContext`
  raised `CurrentName` from three places, each listing the property itself; adding `CurrentAddress`
  meant three more chances to forget one, and the failure is silent — the header keeps showing the
  previous shop after a switch. Factor the set into one `NotifyDisplayChanged()` and assert in a test
  that every trigger raises all of it.
- **A theme-only harness when the user's app is running (2026-07-27).** `uicheck` opens real windows,
  which means the real SQLite file and `credentials.json`. When the user has the application open,
  do NOT run it — build a harness that merges only
  `pack://application:,,,/CameywareOrder;component/Themes/AppTheme.xaml` and rebuilds the control
  under test from a XAML string via `XamlReader.Parse`. It exercises the shipping resource
  dictionary (not a copy of it) and touches no user data. `scratchpad/menucheck` is the worked
  example. Reference the DLL from a scratch `-p:OutputPath`, since `bin\Debug` is locked and stale
  while the app runs.
  - Gotcha: a `ContextMenu` **closes itself when its window loses foreground**, and whether a fresh
    process wins foreground is not yours to decide. Open it in an activate-and-retry loop or the
    run is flaky.
  - Render the `ContextMenu` itself, not the host window — like any popup it is absent from the
    window's `RenderTargetBitmap`.
- **A TextBox and a ComboBox measure to DIFFERENT heights from the same font and padding
  (2026-07-28).** Measured in `ThemedTextBox` / `ThemedComboBox` at 15px with padding 11,9: TextBox
  **39.95**, ComboBox **38**. The cause is structural, not arithmetic — the TextBox's border is on
  the Border that WRAPS `PART_ContentHost`, so it adds 2px to the measure, while the ComboBox's
  border is on the `PART_ToggleButton` sitting BESIDE the content in a Grid, overlaying it, so it
  adds nothing. `MinHeight` 38 hides this at 13px (both land under it and get pinned) and exposes it
  at 15px, where only the TextBox clears it.
  - Where the two sit in one column, **pin an explicit shared `Height`** so they are equal by
    construction. Adjusting padding to compensate does not work and I wasted a round proving it —
    both were already under `MinHeight`, so the padding never governed.
  - Diagnose by DUMPING the visual tree with `ActualHeight` / `DesiredSize` / `Margin` per element.
    Reading the XAML and doing the arithmetic gave the wrong answer twice, including the wrong
    direction of the mismatch. `scratchpad/logincheck` has the dump helper.
- **A TextBox applies its `Padding` itself (2026-07-27).** `PART_ContentHost` already honours it, so
  a custom template that ALSO sets `Margin="{TemplateBinding Padding}"` applies it twice — text boxes
  measured 47px next to a 33px DatePicker on the same row. Every input now carries `MinHeight` 38 so
  a row lines up regardless of padding differences.
- **Typography is modular: three families, six sizes, semantic styles (2026-07-27).** An audit found
  17 sizes in use (11.5 / 12.5 / 13.5 / 14.5 among them), so the same kind of label was a different
  size on two screens. Now: `AppFontFamily` for prose and labels, `NumericFontFamily` for figures
  compared down a column (same face, **tabular numerals** so decimal points align), `IconFontFamily`
  for the Segoe MDL2 glyph set — and a scale of 11 / 12 / 13 / 15 / 18 / 22 exposed as
  `FontSizeCaption` … `FontSizePageTitle`.
  - Screens should say WHAT text is (`PageTitleText`, `ValueText`, `CaptionText`, `MoneyText`,
    `IconGlyph`), not how big it is. Reach for a raw `FontSize` only when nothing fits; if that
    happens twice, add a style to the theme instead.
  - **`NumericCellText` sets NO size and NO colour on purpose.** An orders-list row takes its size
    from the font-size slider and its colour from the completed/refunded gray-out trigger; setting
    either in the cell style overrides both.
  - An implicit `Style TargetType="Window"` carries the family and the base size, so everything
    inherits without each window restating it.
- **Panels open and close with one global transition (2026-07-27).** `Animations/PanelTransition.cs`
  — attached `Mode` (None / Fade / FadeSlide), 0.5s, `CubicEase` EaseInOut, 10px slide; duration and
  curve are defined once. Opt in with `anim:PanelTransition.Mode="FadeSlide"`.
  - **It never assigns `Visibility`.** A local assignment permanently replaces any `{Binding}` on
    that property, and several panels here are bound. The closing half animates Visibility with an
    `ObjectAnimationUsingKeyFrames` track (`FillBehavior.Stop`), which outranks the binding while it
    runs and hands the property straight back afterwards.
  - **The closing animation re-shows the panel, so it re-enters.** By the time `IsVisibleChanged`
    fires the panel is already gone, so the storyboard puts it back at t=0 to have something to
    fade — which raises the event again. `IsAnimatingProperty` guards it, and is cleared one
    dispatcher turn AFTER completion so the property reverting to its real value is suppressed too.
  - The `!element.IsLoaded` test is what stops every window playing its whole set of panels on first
    show.
- **Navigation is split by WHAT the control acts on (2026-07-27).** Order actions (Add / Edit / Delete / Refresh)
  live in the records panel's own action bar, beside the records they operate on; the top bar is
  SYSTEM only — Local Configuration on the left, and on the right the identity block in a fixed order: greeting →
  language → Store Members → Sign Out. The greeting (`Main.Greeting`) uses the account's DISPLAY NAME when it
  has one; a user name is what you sign in with, not what anybody calls you.
  - The top bar is a `Border` + `Grid`, **not a `ToolBar`** — a ToolBar cannot right-align part of its
    content and adds an overflow chevron nobody asked for.
  - `MainViewModel.FilteredCount` backs the count badge; it is raised everywhere `PageSummary` is,
    because both derive from `_filteredCount`.
- **Seeded data must populate `Garments`, not the legacy measurement fields (2026-07-27).**
  `Order.HasCustomMadeService` and `CustomMadeMeasurementReader.GetGarmentNames` both read
  `record.Garments`; the flat `JacketLengthCm` / `ShirtChestCm` fields only migrate into it when a
  record is re-saved through the editor. The first mock-data run filled only the legacy fields, so
  every seeded custom-made order reported `CustomMade.Flag.No` in the Custom Service column. Anything that writes
  `CustomMadeServiceRecord` outside the editor has to build `Garments` with real predefined garment and
  term ids (`jacket`/`shirt`/`dress`/`qipao`… × `length`/`chest`/`sleeve`…).
- **The printed receipt is panels, not a column (2026-07-27).** `ReceiptCard(background, topBorder)`
  wraps the customer block and the totals block in padded `Section`s (the totals one tinted, with a
  2px top rule) so the page reads as a few groups; section titles are primary-coloured; info-line
  leading is 3px, not 1px; page padding is 48/40. The payment-or-refund narrative lives in
  `AddReceiptPaymentNarrative` OUTSIDE the totals panel — folding explanation into it dilutes the one
  block the eye is meant to land on.
- **Activation is per MEMBERSHIP, not per account (2026-07-27).** `ShopMembership` (one record per person
  per shop) carries `Roles`, `IsActive`, `JoinedOn`, `DeactivatedOn` and a `TimeOnly?` shift. It replaced
  the flat `ShopAssignment` (shop, role) pairs, because activation and shift are facts about a person AT A
  SHOP and would have had to be duplicated across each role row.
  - **The rule that forced this shape:** deactivating someone at one branch must not cost them their job at
    another. So sign-in is refused only when the account belongs to ≥1 shop and EVERY membership is
    inactive. `Authenticate` returns `SignInResult` with a `SignInFailure` so the login window can say
    "your account has been deactivated" instead of "wrong password" — the credential WAS right, and
    retyping it will never help.
  - An account with **no memberships at all is not "deactivated"** — that is a new hire nobody has posted
    yet. They sign in and get the accurate "no shop is available" message. Do not collapse these two.
  - `DeactivatedOn` is stamped by `ApplyProfile` on the active→inactive TRANSITION and cleared on the way
    back. It is never typed: "when were they delisted" is a record of what happened.
  - Account-level vs membership-level is a real line: **name and birthday are account-level** (a person has
    one birthday), **shift, join date and activation are membership-level** (the same person can work
    different hours at two branches).
  - `UserManagementWindow`'s matrix sends **roles only** (`SetShopRoles`), so an administrator editing
    roles there cannot silently reset the roster's activation, start date or shift. Any new writer of
    memberships must preserve the fields it does not own.
  - Guards that exist for a reason: a manager cannot deactivate their OWN membership of the shop they are
    standing in (they would revoke the screen they are on), and `CanSetPasswordFor` lets a manager reset a
    password only when the target works exclusively in shops that manager runs — otherwise a branch
    manager could take over an account belonging to a branch they have nothing to do with.
- **Authorization is PER SHOP, and the answer changes when the shop does (2026-07-27).** An account is
  either an administrator (everything, everywhere — an account-level `IsAdministrator` flag, never a shop
  assignment) or it holds a set of `ShopAssignment`s: one or more roles per shop. Holding Manager AND Staff
  in the same shop is legal and resolves to Manager, which is why the management UI is a checkbox MATRIX
  rather than a per-shop dropdown.
  - `UserRole`'s declaration order is LOAD-BEARING: values are strongest-first (Admin 0, Manager 1,
    Staff 2) and `StrongestRole` takes the **minimum**. Inserting a value in the middle re-ranks the rest.
  - Assignments key on **`Shop.PublicId`, never `Shop.Id`** — `credentials.json` lives outside the
    database and whole databases move between machines, where the local autoincrement ids collide.
  - Capabilities are named, not role comparisons: `CanCreateShops` / `CanManageUsers` / `CanUseDataTools`
    are administrator-only because they act on the whole installation; `CanConfigureShop` is administrator
    **or the open shop's manager** and gates Shop Settings / Currency Setup / Measurement Terms / Header & Footer. The old single
    `CanManageShops` is gone — it conflated "may create a branch" with "may configure this one".
  - `AuthenticationService.BindShop` supplies the shop the capabilities resolve against and is called from
    `App.ApplyActiveShop` **BEFORE** `ShopContext.SetActive`. Order matters: `SetActive` raises
    `ShopChanged`, and `MainWindow` re-gates its chrome from that event — bind afterwards and the window
    repaints with the previous shop's permissions.
  - **`MainWindow.ApplyRolePermissions` MUST stay subscribed to `ShopChanged`.** It used to run only in
    the constructor, which is correct exactly until someone who is a manager in one branch and staff in
    another uses Switch Shop. Same reason it re-runs after User Management closes.
  - The database path in the status bar is hidden with the data tools it describes — it is the same
    information those menus act on, printed rather than clicked.
- **`credentials.json` is schema version 2, and the upgrade is deliberately in TWO halves.** The service is
  constructed for the login window, which runs before the generic host exists — so no shop can be read at
  that point. `UpgradeAccountShape` (on load) promotes a legacy global `Role=Admin` to the admin flag;
  `ApplyLegacyShopAssignments` (called from `App` right after the shop bootstrap, before the first shop
  list) grants a legacy Manager/Staff that role in every shop that then exists, which is exactly what they
  could already open. It also refreshes the signed-in session, because the user authenticated BEFORE the
  migration ran and would otherwise be told no shop is available.
  - `ProvisionedAccounts` records every account ever seeded, so **deleting a seeded account sticks**.
    Topping seeds up on every load (the old behaviour) would have made the delete button useless.
    `admin` is the one exception and is always restored: an installation with no administrator can never
    be administered again. Nothing can promote another account to administrator.
  - **ONE account is seeded: `admin` (v9.2).** It used to be five — `manager`, `staff`, `test1` and
    `test2` as well, each with its user name as its password, so the assignment screen had something
    to exercise. That was a development convenience billed to every shop that installed the product.
    Do not re-add them; seed data for a demonstration is what the demo store is for.
- **Tax is a STORE rule, not a per-order figure (2026-07-27).** `PaymentTaxRules` (in **Models**, not
  Services) holds one `PaymentTaxRule` — taxable + rate — per payment method, persisted on
  `Shops.PaymentTaxRulesJson` and edited in Local Configuration → Shop Settings. Its static `Active` is assigned in
  `App.ApplyActiveShop` alongside the currency/terms binds.
  - It lives in Models because `Order.CalculateSectionPayment` must consult it; a model reaching into a
    service would be worse than a model owning the rule type. Defaults (cash + e-transfer free, both card
    types 13%) mean a shop that never opens the settings screen behaves exactly as the app always did.
  - **The gate changed, the rate did not.** `CalculateSectionPayment` now taxes a portion when
    `Active.IsTaxable(method)` rather than `method == Card`, but still at the rate stored **on the order**.
    So a saved order never silently re-prices, while a method the shop makes tax free stops adding tax.
  - `OrderEditWindow` is the other half: it resolves rates live from `Active` for an editable order, and
    keeps the stored rates for a **read-only** one (completed/shipped/cancelled/returned) whose receipt is
    already printed. That is the whole answer to "does a rate change affect existing orders".
  - The three tax-rate `TextBox`es are gone — they are bold read-only value blocks (`LockedRateBox` /
    `LockedRateText`). Deliberately not a disabled TextBox: a greyed box invites clicking and reads as broken.
- **`PaymentMethod.Card` is legacy and must never be deleted.** Debit and credit are now separate values
  (`DebitCard = 5`, `CreditCard = 6`); orders saved before the split still hold `Card = 2`. Everything that
  DISPLAYS a method runs it through `PaymentTaxRules.Normalize` (→ `DebitCard`, which is what the old label
  the debit-card option actually named): `SetSelectedDownMethod`/`SetSelectedFinalMethod`,
  `OrderEditWindow.PaymentMethodName`, `OrderPaymentSummaryConverter.MethodText`. Without the normalization in
  the setters, a legacy order comes back with **no deposit radio checked**, and `UpdateSectionVisibility`
  collapses the section's whole pricing panel — the failure mode already recorded twice in this file.
- **The payment-radio helpers take `PaymentSectionControls`, not a positional radio list.** With five deposit
  radios and four final ones, `GetSelectedDownMethod(none, etransfer, card, cash)` was one argument swap away
  from compiling and silently reading the wrong method. `GetSelectedFinalMethod`/`SetSelectedFinalMethod`
  replaced `Get/SetSelectedPaymentMethod`.
- **Receipt numbering is per shop** (`Services/OrderNumberFormatter` + five `Shops.OrderNumber*` columns):
  Timestamp (the legacy `ORD-yyyyMMdd-HHmmss`, still the default so no existing shop's numbering changes),
  Sequential, DailySequential, YearlySequential, each with a prefix and padding.
  - The counter advances in `CommitSequence` **only after the order is saved** — a preview shown in an
    abandoned form must not burn a number, because a gap in a receipt run is what an audit asks about.
  - **Bug caught by testing, worth remembering:** `ResolveNextSequence` compared the period key in every mode,
    so a continuous run carrying a stale key restarted at 1 and re-issued numbers already given to customers.
    Only period-based modes may roll over; a continuous run has no period and must never restart.
  - `Reserve` also scans for numbers already taken — order numbers can be typed by hand and databases get
    imported, so a stored counter alone is not proof a number is free.
- **`ShopColumnMigrations` (App.xaml.cs) is the Shops equivalent of `OrderColumnMigrations`.** The
  `CREATE TABLE IF NOT EXISTS Shops` guard only helps a FRESH install; an existing database already has the
  table, so every column added later needs its own ALTER. Keep the two lists in step — a column added to one
  and not the other works on exactly one kind of installation.
- **The shop's GST/HST number lives in the branding settings**, not on the Shop row:
  `ReceiptBrandingSettings.TaxRegistrationNumber`, edited directly under the Header card in Header & Footer and
  printed under the header by `InjectReceiptBranding` (inserted BEFORE the header is prepended, so the header
  lands above it) and by the QuestPDF measurement export. It is NOT per language — a registration number is
  the same string in both; only its label is translated (`Receipt.TaxNumberLine` carries the whole line shape).
  Each language tab shows its own box over one shared value, reentrancy-guarded by `_syncingTaxNumber`.

- **2026-07-27, OrderEditWindow CS0103 storm — diagnosed, code was correct.** ~80 CS0103
  errors on `x:Name` controls and `InitializeComponent` while `dotnet build` reported
  0 errors / 0 warnings. Proof it was the stale design-time model, not a defect:
  `obj\Debug\net8.0-windows\Views\OrderEditWindow.g.i.cs` held **97** fields at
  **9:06 AM** while `OrderEditWindow.g.cs` held **147** at **2:09 PM** — the design-time
  partial was missing 50 controls, among them `ClearAllBalancesStrike` and
  `AlterationDownCompletedStrike`. Every other `*.g.i.cs` shared the same 9:06 AM stamp,
  so the whole design-time build was idle. The stale file was deleted (a regenerable
  `obj/` artifact); clearing it fully needs a language-server restart from the IDE.
  - **CONFIRMED at 14:58**: after the restart, all 15 `*.g.i.cs` regenerated in one pass
    and `OrderEditWindow.g.i.cs` came back with **147 fields — identical to `.g.cs`** —
    carrying the two markers (`ClearAllBalancesStrike`, `AlterationDownCompletedStrike`)
    it had been missing. Every CS0103 **and all 4 SonarLint findings** cleared together,
    which is what the CLI analysis had already predicted by reporting zero. Not one line
    of application code was changed to fix ~80 editor errors — the whole episode was one
    stale artifact. **The field-count comparison is the reliable test both before and
    after: it proves the diagnosis up front and proves the fix landed afterwards, without
    trusting a Problems view that can be quiet for the wrong reason.**
  - **It came back ~2 minutes later on `CustomMadeServiceWindow.xaml.cs`** (fresh
    document, `modelVersionId: 1`) while that window's `.g.i.cs` was correct and current
    at 37/37 fields — the "name IS present yet reported missing" signature, i.e. the
    server had stopped reading the generated files again. `dotnet.restartServer` fixed
    it a second time. Suspected trigger: a CLI `dotnet build` run between the two,
    rewriting `obj/` under the live IDE. NOT proven, so it is written as a suspicion —
    but the practical rule stands either way: **batch edits and build once**, rather
    than building between every check, while someone has the project open.
  - **Dead end, do not retry:** redirecting CLI builds away from the IDE's folder with
    `-p:BaseIntermediateOutputPath=obj-cli\` FAILS on this project. WPF's temp-project
    mechanism (`*_wpftmp.csproj`) ignores the override, reads stale `AssemblyInfo` files
    out of the real `obj/`, and emits a wall of CS0579 duplicate-attribute errors. It
    also leaves `*_wpftmp*` residue behind (420 files were cleared afterwards). Recovery
    is `Remove-Item obj -Recurse -Filter '*_wpftmp*'` plus deleting `obj-cli`/`bin-cli`.
  - Likely trigger: adding then removing the `SonarAnalyzer.CSharp` PackageReference in
    the preceding turn rewrote the `.csproj` and `project.assets.json` twice, and C# Dev
    Kit reloads the project on such a change. **Expect an IDE model refresh to be needed
    after any csproj edit**, including temporary analyzer packages.

- **NEVER round-trip a project file through `Get-Content -Raw` / `Set-Content` in Windows PowerShell 5.1
  (2026-07-27).** `Get-Content` decodes with the ANSI codepage (1252 here), so every UTF-8 byte becomes a
  mojibake char, and `Set-Content -Encoding utf8` then writes that back double-encoded — Local Configuration came back
  as `æœ¬åœ°é…ç½®` in two XAML files. It also adds a BOM. Recovery is byte-level: read the file, decode
  UTF-8, re-encode with codepage 1252, write the raw bytes (verify the result contains a known string
  first). Use the Edit/Write tools for file content; keep PowerShell for running commands.
- **S2325 on a WPF status/message helper has an honest fix, not just a suppression (2026-07-27).** Four
  helpers in `UserManagementWindow` that only wrote to `x:Name` controls were flagged "make static" — the
  documented false positive, and making them static would not compile. Rather than suppress, they were
  changed to take a string-table **key** and resolve it through `_localization`, so each genuinely reads
  instance state. The call sites got shorter too. Prefer this shape for any new message helper.
- **Verifying a UI redesign needs the window OPENED, not just built.** `uicheck` in the session scratchpad
  constructs the real windows against the built dll with a `PresentationTraceSources` listener attached and
  renders each to PNG. It caught two wording bugs a build never would (a subtitle duplicated by the list
  caption, and an empty-state that said "no shops have been created" when the real cause was "none is
  assigned to you"). Two gotchas if it is rebuilt: `Application.ResourceAssembly` is latched before your
  first statement can set it, so give the HARNESS its own `Assets\ICONS\app-icon.ico` `<Resource>` instead;
  and `System.IO` needs an explicit `using` there.
- **Running Sonar without SonarLint**: when the IDE/SonarLint tooling is unavailable, add
  `SonarAnalyzer.CSharp` as a PackageReference, `dotnet build`, read the `warning Sxxxx` lines,
  then remove the package again. SonarLint *is* this analyzer, so rule ids and messages match the
  IDE exactly. Verified 2026-07-27: it found 11 issues the plain build reported as 0 warnings.
  - **A full build sees the XAML-generated `.g.cs` fields that standalone single-file analysis
    cannot.** So an S2325 "make static" raised THIS way is far more likely to be real than the
    documented SonarLint false positive — check whether the method truly touches an `x:Name`
    control before either complying or dismissing it. `MeasurementTermsWindow.ShowDuplicateTermWarning`
    was a genuine one and is now static.
  - It does NOT see XAML data bindings. `ShopPickerWindow.ShopRow.Name` is bound as
    `{Binding Name}` and was flagged S1144 "unused"; it carries a justified `[SuppressMessage]`.
    Deleting a "dead" member on a class used as a template item is how a list silently goes blank.
- **Every `Regex` in this project carries a match timeout** (S6444). Each file declares a
  `private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1)` **above** its patterns:
  static field initializers run in TEXTUAL order, so a timeout declared below them is still
  `TimeSpan.Zero` when the `Regex` constructors run, which `Regex` rejects — surfacing as a
  `TypeInitializationException` on the first keystroke rather than as a build error. Keep new
  patterns below that field.

- **The GraphQL endpoint must never be able to stop the app.** Nothing in the desktop UI reads
  through it (no `HttpClient`, no in-app reference to the URL) — it exists for external callers,
  while the UI goes through `AppDbContext` directly. Until 2026-07-27 a fixed `UseUrls(...:5050)`
  plus an unguarded `_host.StartAsync()` meant a second running copy of the app made startup fail
  with `IOException: Failed to bind to address http://127.0.0.1:5050: address already in use`,
  which `OnStartup`'s catch turned into `Shutdown(1)` — no orders readable because a port nobody
  reads was busy. Now `ResolveServerPort()` prefers 5050 and otherwise resolves a **concrete** free
  port (never `localhost:0` — Kestrel resolves that hostname to two loopback addresses and takes a
  separate ephemeral port for each), and `StartApiServerAsync()` catches `IOException` and runs
  without the API. `App.ApiEndpoint` holds whatever was actually bound, read back from
  `IServerAddressesFeature`. General rule: an auxiliary service's failure must degrade the feature
  it provides, never the application around it.
  - The catch is `IOException` ONLY, on purpose — a broader one would hide a genuinely broken
    hosted service behind a "everything is fine" start.
  - A busy 5050 almost always means another `CameywareOrder` process, sometimes one with no
    window at all. `Get-NetTCPConnection -LocalPort 5050` names the owner.

- **Shop isolation rests on two mechanisms in `AppDbContext` — do not work around either.**
  - `HasQueryFilter(e => e.ShopId == _shopId)` on `Order` confines every read. `_shopId` MUST stay
    an instance field read in the constructor: EF parameterises instance-field references, while a
    static lookup would be baked into the compiled query and only the first shop opened would ever
    work. `IgnoreQueryFilters()` is the escape hatch for a future cross-shop view.
  - `SaveChanges(bool)` / `SaveChangesAsync(bool, ct)` stamp `ShopId` + `CurrencyType` on added
    orders from `ShopContext.RequireCurrent()`. Never set `ShopId` at a call site: `CopyOrderAsync`
    and the GraphQL create mutation build orders from explicit property lists, and one that forgets
    it saves silently to shop 0 and then vanishes from every view. This was observed for real.
- **`Find`/`FindAsync` BYPASS query filters** — they are key lookups. Any code reaching an order or
  order item by id must use a LINQ query (`FirstOrDefaultAsync(o => o.Id == id)`) or it can read,
  mutate and delete another shop's data. This had to be fixed in four GraphQL resolvers and
  `MainViewModel.DeleteOrderAsync`. `OrderItem` has no shop of its own, so reach items **through**
  the filtered `Orders` set.
- **Lock state must be driven from ONE trigger, and lock helpers must assign both ways.** Two
  bugs of the same shape, both in `OrderEditWindow`:
  - `ApplySectionInputLocks` (`IsReadOnly`) ran on every refresh via `RefreshPricingLocks`, while
    `ApplySectionLock` (`IsEnabled`) ran only via `UpdatePaymentVisibility`, which
    `RefreshComputedTotals` skips when `runAutoComplete: false`. Both now depend on values a plain
    refresh changes — the alteration category (`IsServiceSwitchedOff`) and the section total
    (`IsSettled`) — so the deposit radios/checkboxes stranded in a stale state while the price box
    unlocked correctly. `RefreshPricingLocks` now applies BOTH. Safe because `ApplySectionLock`
    only assigns `IsEnabled` and writes no text, so it cannot re-enter the refresh.
  - `ApplySectionLock` set `DownpaymentBox.IsEnabled = false` but never back to `true` — the
    re-enable lived in `UpdateSectionVisibility`. A helper that can only lock is exactly how a
    control gets permanently stranded; it is now assigned unconditionally in both directions.
- **`ExecuteSqlRawAsync` treats its SQL as a COMPOSITE FORMAT STRING.** A literal `{` or `}` in
  raw SQL is parsed as a parameter placeholder and throws `FormatException: expected an ASCII
  digit` before any statement runs. This bit the `Shops` DDL, where `NamesJson TEXT NOT NULL
  DEFAULT '{}'` was rejected outright. Never put braces in raw SQL except a real `{0}` parameter;
  if a JSON default is needed, set it from the model's property initialiser instead.
- **Multi-shop: `Shop.PublicId` (Guid) keys everything stored OUTSIDE the database.** `Shop.Id` is
  a local autoincrement and whole databases move between machines via `GlobalSettingsPackage` /
  `DatabasePathProvider.ImportDatabaseFrom`, so two installs will allocate the same `Id`. Per-shop
  files (measurement terms, branding folder) must key on `PublicId` or an import silently hands one
  shop another shop's settings.
- **`Shop` name is bilingual (`NamesJson`), not a single string** — it replaces the localized
  `Main.HeaderTitle` in the header and on printed receipts, so one string would force one
  language's users to read the other's name. Same per-language dictionary pattern as
  `MeasurementTerm.Names`.
- **`Orders.ShopId` is a scalar with no FK and no navigation** — SQLite cannot add a foreign key to
  an existing table without rebuilding it, and `Shops` is created by a runtime DDL guard. The index
  `IX_Orders_ShopId` is what the shop-filtered list actually needs. `ShopId = 0` means "unassigned"
  and is what `EnsureShopBootstrapAsync` claims.
- **Startup schema phase order is LOAD-BEARING**: `EnsureMigrationBaselineAsync` →
  `MigrateAsync()` → `EnsureSchemaCompatibilityAsync` (the column guards). The guards used to run
  FIRST and returned early when the `Orders` table did not exist, so on a machine with no
  `orders.db` all 38 `ALTER TABLE Orders ADD COLUMN` statements were skipped — the two migrations
  create 15+3 columns against a model of ~50, so **a fresh install crashed on the first order
  query**. Never reorder these three. `OrderColumnMigrations` and the three columns owned by
  `AddOrderPaymentFields` deliberately do not overlap, which is what makes migrate-before-guards
  safe.
- **NEVER run `dotnet ef migrations add` on this project.** `Migrations/AppDbContextModelSnapshot.cs`
  records 22 `Order` properties against the model's ~50, so a scaffolded migration emits
  `AddColumn` for ~28 columns that already exist and the next `MigrateAsync` fails with
  "duplicate column name" on every live installation. Add an `OrderColumnMigrations` entry for a
  new column, or a `CREATE TABLE IF NOT EXISTS` guard for a new table. Adopting migrations
  properly means regenerating the snapshot first, as its own task.
- **`OnStartup` is `async void`** — its body lives in `StartApplicationAsync` wrapped in
  try/catch → dialog → `Shutdown(1)`. Without that, anything thrown past the first await made the
  app disappear silently.
- **Verifying SQLite schema without a CLI**: SQLite keeps each table's `CREATE TABLE` text and its
  row data as UTF-8 inside the `.db` file, and rewrites that text on `ALTER TABLE ADD COLUMN`. So
  `grep -a` / a UTF-8 read of the file confirms which columns exist and which migration ids are
  recorded — no sqlite3 tooling required.

- **A service can be switched OFF, not just left empty**: `Alteration.Category.None` (tag
  `"None"`, stored in `Order.ServiceDetails` like the other categories, listed **FIRST** so it
  is the default for a new order) marks the order as having no alteration work.
  - GOTCHA: the edit-load fallback for an unmatched category must NOT be `SelectedIndex = 0`
    any more — that is now "None", which would switch a charged legacy alteration service off
    and drop it from the totals. It selects `DefaultSavedAlterationCategoryTag`
    (`GarmentAdjustments`) via `SelectAlterationCategory(tag)` instead. `PaymentSectionControls.ServiceSwitchedOff` is an optional `Func<bool>` — only
  Alterations supplies one — and it feeds three things: `HasItems()` returns false,
  `RefreshAlterationTotals` uses a price of 0 (the box VALUE is kept so switching back
  restores it), and both lock methods plus `AlterationAdditionalNotesBox` go read-only.
  `AlterationCategoryBox` itself stays enabled — it is the only way back out of "None".
  - `OnServiceCategoryChanged` therefore runs the FULL `RefreshComputedTotals`, not just the
    breakdown: the category now affects totals and locks.
- **One money-input behaviour for every price box**: `RegisterMoneyBox(box, restoreZeroOnBlur)`
  wires decimal + paste filtering, clears a box already showing 0 on focus (so typing "12"
  gives "12", not "012"), and optionally restores "0" on blur. Applied to the alteration price,
  all tax boxes, all deposit boxes and the runtime-created clothing price boxes.
  - `OnMoneyBoxGotFocus` skips **read-only** boxes as well as disabled ones — a read-only box
    still takes focus and clearing its text programmatically succeeds.
  - `restoreZeroOnBlur: false` where BLANK has its own meaning: the promotional price (blank =
    no promotion) and the alteration price (blank = service absent, per `HasItems` — forcing
    "0" would enrol it as an unpriced service).
- **Deposit is capped at the pre-tax subtotal, visibly**: `EnforceDepositCeiling` warns and
  pins the box to `SectionSubtotal()` (pre-tax — `SectionTotal` is post-tax and too generous).
  `CalculateSectionPayment` already clamped silently, which hid typos behind numbers that
  quietly stopped responding. Needs its own `_enforcingDepositCeiling` guard because the modal
  pumps messages and the correction re-raises TextChanged.
- **Pick-up asks before completing an unpriced order**: `ConfirmPickUp()` lists any service
  with items but no charge and warns that completing makes the order read-only; declining
  reverts the tick inside the `_syncingStatus` guard. Shares `UnpricedServiceList()` with the
  clear-all warning so both use one definition. A fully priced order is not interrupted.

- **One-click global settings backup**: `Services/GlobalSettingsPackage` bundles everything
  local into a single zip — `settings.json` (currency, language code, `MeasurementTermsConfig`,
  `BrandingExport`, version + timestamp) plus a **nested** `database.zip`. Nesting
  `DatabasePathProvider.ExportDatabaseTo` rather than re-implementing it keeps ONE code path
  for the db + WAL/SHM sidecars + `Documents/` tree, and restore reuses `ImportDatabaseFrom`
  with its auto-backup and zip-slip guarding. `TryRead` validates with no side effects so the
  destructive confirm is only offered for a real package; `Import` applies only the sections
  present, so an older/partial package never blanks out what it does not know about.
  `ReceiptBrandingStore.BuildExport()` was extracted so the package embeds the export OBJECT
  rather than nesting a JSON string inside its own JSON.
  - Import/Export submenu order is now HeaderFooter → MeasurementTerms → LocalDatabase →
    (separator) → GlobalSettings.

- **"Cleared" is not the same as "settled" — always pair it with a charge**: a section with
  no charge reports cleared because nothing is owed (`IsSectionCleared` returns true on
  `total <= 0`). Locking on that tick alone disabled the deposit radios, deposit box, tax box
  and item editors, so the section stopped responding and could never be given a price to
  un-clear it. `OrderEditWindow.IsSettled(c)` = `BalanceClearedCheck.IsChecked is true &&
  SectionTotal() > 0m` is now the single test behind every settlement lock
  (`ApplySectionLock`, `ApplySectionInputLocks`, both blocks in `RefreshPricingLocks`,
  `RefreshCustomMadeButtonLabel`, `OnEditCustomMadeRecordClick`). Never re-introduce a bare
  `BalanceClearedCheck.IsChecked` lock test.
- **Persist on `HasItems()`, never on `SumTotal > 0`**: `ApplyPaymentFields` used to drop a
  zero-charge section into `ClearSectionPaymentFields`, nulling its downpayment method,
  deposit-received and cleared flags. On reload the null method leaves every deposit radio
  unchecked, and `UpdateSectionVisibility` collapses `PricingPanel` when nothing is selected —
  the deposit box, breakdown and final block all vanish and the section looks broken. All
  three gates now use `HasItems()`. This also makes `AlterationSubtotal` persist as `0`
  rather than `null`, so a zero-priced section survives a save/reopen round-trip.
  - Still open: `Order.IsBalanceCleared` early-returns on `TotalAmount <= 0m`, so an order
    whose services are ALL zero-priced still reads Outstanding once saved.

- **One tax label, not two**: `Order.Fields.DepositTax` was deleted and all six tax rows
  in `OrderEditWindow.xaml` (3 deposit-stage + 3 final-stage panels) now bind
  `Order.Fields.ServiceTotalTax`. Both had displayed the identical `money.Tax`; because the
  two panels are mutually exclusive, the same figure was just called two different things
  depending on stage. Do not reintroduce a stage-specific label for a section-wide value —
  the stage-specific keys are `Order.Fields.DepositTaxRate`/`FinalTaxRate` (the editable
  rate) and `Order.Fields.DepositTaxLine`/`FinalTaxLine` (the per-portion split lines).
- **String table is orphan-free as of 2026-07-26**: 330 keys per block, both blocks
  identical. 23 dead keys were pruned. To re-audit: extract every `<Text key>` and grep the
  source for each, EXCLUDING the interpolated families — `Measure.Term.{id}`,
  `Measure.Garment.{id}`, `ClothingItem.{key}`, `PaymentMethod.{m}`, `AgeType.{t}`,
  `CurrencyType.{c}`, `ReturnReason.{c}`, `ServiceType.{t}`, `Alteration.Category.{t}`,
  `OrderEdit.Panel.{enum}` — which are live despite having no literal reference.
- **Dead but DB-backed (left in place deliberately)**: `Order.ChestSize` /
  `Order.JacketLength` are written as `null` on every save and never read (superseded by the
  Measurement Terms system); `Order.CurrencyType` is unused (currency is global). They
  survive only as field copies in `MainViewModel.CopyOrderAsync`. Removing them is a
  migration decision. By contrast `Order.Subtotal`/`TaxRate`/`Downpayment`/
  `DownpaymentMethod`/`FinalBalanceMethod` DO still participate as legacy fallbacks.

- **"Order items", not money, decide whether a service takes part**: `PaymentSectionControls`
  carries `HasItems()` (custom-made records exist / clothing rows exist / for Alterations a
  non-empty price box, since it has no item list), `SectionTotal()` and `HasMissingPrice`
  (has items but total ≤ 0). Used by BOTH `ApplyClearAllToSection` and the Order.Fields.AllServicesTotalAmount
  breakdown, so the two agree. OrderEdit.ClearAllBalances now ticks **the deposit-received box as well as the balance-cleared box** on every
  participating section, defaults a null deposit method to Cash, skips item-less sections,
  and treats an explicit "None" deposit as nothing-to-confirm. A zero-priced service still
  participates: it is flagged amber （价格有误） in the breakdown and named in a
  non-blocking warning dialog (`OrderEdit.Warn.UnpricedServices`).
  - GOTCHA: `IsOrderBalanceCleared` used to early-return false on `_totalAmount <= 0m`.
    Because `RefreshPaymentSummary` writes `ClearAllBalancesCheck.IsChecked = cleared` from
    derived state, that made the clear-all tick spring straight back off for an all-zero
    order. It now gates on "no section has items".
  - KNOWN LIMITATION: `Order.IsBalanceCleared` still gates on `TotalAmount <= 0m`, and
    `ApplyPaymentFields` persists a zero-total section as absent (`XxxSubtotal = null`), so an
    order whose services are ALL zero-priced reads Outstanding once saved. Aligning that
    reaches into `XxxAddedToReceipt` and the printed receipt.
- **`Order.Fields.ReceivedDownpayment` / `.ReceivedFinalBalance` only count after their checkbox**: `Order.ReceivedDownpayment` sums
  through `SectionReceivedDeposit(money, XxxDownpaymentCompleted)` and the editor mirrors it —
  a typed deposit is what the shop EXPECTS, not what it holds. `ReceivedFinalBalance` was already gated on
  `BalanceCleared`. Both model and editor were changed together so a saved order reports the
  same figures the editor showed.

- **Payment breakdown row layout (as of 2026-07-26)** — both panels now carry a pre-tax
  final-balance row (`Order.Fields.PreTaxFinalBalance` 税前尾款, value `money.FinalBase`):
  - Deposit stage (`*DepositBreakdownPanel`, 4 rows): PreTaxSubtotal, **PreTaxFinalBalance**,
    ServiceStageTax, PostTaxTotal.
  - Final stage (`*FinalBreakdownPanel`, 7 rows): PreTaxServiceTotal, PreTaxDownpayment,
    **PreTaxFinalBalance**, ServiceTotalTax, the per-portion tax-split `StackPanel`,
    PostTaxTotal, FinalBalance.
  - GOTCHA when inserting a row here: a `Grid.Row` beyond the `RowDefinitions` count is
    CLAMPED by WPF, not an error — a renumbering mistake shows up as two rows silently drawn
    on top of each other, never as a build failure. Add the `RowDefinition` and renumber every
    following row in the same edit, and do it per section rather than by find/replace.
  - Three distinct "balance" keys — do not conflate: `PreTaxFinalBalance` (税前尾款, before
    tax), `FinalBalance` (剩余尾款, taxed and still outstanding), `ReceivedFinalBalance`
    (实收尾款, taxed and collected).

- **Small-print breakdowns in the order editor**: two code-filled panels now explain the
  headline figures. `ServicesTotalBreakdownPanel` (under Order.Fields.AllServicesTotalAmount) lists one line per
  charged section with a parenthetical — Alterations → service category, CustomMade →
  measured garment names, ReadyMade → the item categories actually priced — built by
  `RefreshServicesTotalBreakdown`/`AddServiceTotalDetail`. In each section's final
  breakdown, `*DepositTaxLineText`/`*FinalTaxLineText` split Order.Fields.ServiceTotalTax into
  定金（现金）税收 / 尾款（银行卡）税收 via `UpdateTaxBreakdownLines`.
  - RULE: put the **whole line shape** in `Languages.xml`, not just the words. Chinese uses
    fullwidth `（）：` and English ASCII `(): ` — concatenating punctuation in C# produces
    `Alterations（Garment Adjustments）：$123` in English. Keys:
    `Order.Fields.ServiceTotalLine(NoDetail)`, `Order.Fields.DepositTaxLine`/`FinalTaxLine`.
  - `CustomMadeMeasurementReader.GetGarmentNames` gained an `IEnumerable<CustomMadeServiceRecord>`
    overload (the editor holds unsaved records, not an `Order`); the `Order` overload delegates.
  - GOTCHA: the category ComboBoxes (`AlterationCategoryBox` and each clothing row's
    `categoryBox`) had **no change handler at all** — anything newly displaying a category
    must wire one or it goes stale. They call `RefreshServicesTotalBreakdown()` only, since a
    category carries no money.
  - GOTCHA: a brush created in code needs `System.Windows.Media.` qualification (bare
    `Color`/`SolidColorBrush` is ambiguous under ImplicitUsings vs QuestPDF/HotChocolate);
    build it once via `CreateFrozenBrush`, not per line.

- **Tax rate is per PAYMENT STAGE, edited through one shared box**: each section stores TWO
  rates — `Orders.XxxTaxRate` (deposit stage) and the new `Orders.XxxFinalTaxRate` (final
  stage), so a shop can charge e.g. 5% on a card deposit and 7% on the card balance.
  `Order.CalculateSectionPayment` now takes `depositRatePercent` + `finalRatePercent`;
  each `XxxMoney` passes `XxxFinalTaxRate ?? XxxTaxRate ?? 0m`, so legacy single-rate
  orders compute exactly as before (no data fix-up, 3 runtime column guards only).
  - UI: ONE tax-rate box per section (user's choice over two side-by-side boxes). It edits the
    deposit rate until the deposit is received, the final rate afterwards, and its **label**
    says which (`Order.Fields.DepositTaxRate` 定金税率 / `Order.Fields.FinalTaxRate` 尾款税率).
    `PaymentSectionControls` holds `DepositTaxRate`/`FinalTaxRate`/`ShowingFinalRate`/
    `IsFinalStage`; `ApplyStageTaxRates` banks the typed value against the stage the box was
    showing, then resolves both via `ResolveStageRate` (non-card → 0; card with no rate yet →
    deposit falls back to 13%, final falls back to the **deposit's** rate).
  - `IsFinalStage` = `DownNone` checked OR deposit marked received (None = no deposit taken,
    so the outstanding balance is the whole charge).
  - GOTCHA 1: only rewrite the tax box on a stage flip or a rule-forced change. Normalising
    it on every refresh eats a half-typed "5." from under the caret.
  - GOTCHA 2: seed the rates AFTER `LoadPaymentFields` (`LoadStageTaxRates` is called from
    the edit ctor at that point). With no payment radio selected yet, the card/cash rule
    zeroes the rate before it is ever used — which would silently reset a saved 5% to 13%
    on reopen. The pre-existing `ApplyTaxRateRule` had the same latent flaw.
  - GOTCHA 3: the tax box's read-only rule keys off `sectionLocked`, NOT `inputsLocked`.
    `inputsLocked` includes "deposit received", but that is exactly the moment the box hands
    over to the final rate and must become editable again. Price/deposit boxes still use
    `inputsLocked`.

- **Inheritance must be kept live, not just resolved at read time**: because
  `ApplyPaymentFields` persists the final method through `EffectiveFinalMethod`, an
  *inherited* method is indistinguishable from a *chosen* one once reloaded — so a saved
  card deposit kept taxing the balance at the card rate even after the deposit was switched
  to cash. `PaymentSectionControls.FinalMethodUserChosen` now tracks the difference: set when
  the user clicks a final-method radio, and `ApplyDepositMethodChange` re-mirrors the final
  method onto every deposit-method change while it is false. `LoadPaymentFields` recovers the
  flag with `InferFinalMethodWasChosen` — a stored final method that DIFFERS from the deposit
  must have been deliberate; an equal one counts as inherited.
  - GENERAL LESSON: whenever a derived value is persisted, persist or reconstruct the fact
    that it was derived. Otherwise the next load promotes it to user intent.
  - NOT A BUG (expect this question again): when both portions share a method AND a rate, the
    section's total tax is invariant to the deposit split —
    `deposit×r + (subtotal−deposit)×r ≡ subtotal×r` — so the deposit-stage breakdown rows do
    not move when the deposit amount changes. They only move when the two portions differ in
    method or rate.
- **Final balance inherits the deposit's payment method until explicitly chosen**:
  `OrderEditWindow.EffectiveFinalMethod(PaymentSectionControls)` resolves the final
  method as `explicit selection ?? deposit method` (`None` never inherits). It is used by
  all 3 `Refresh*Totals` AND by `ApplyPaymentFields` on save, so persisted and on-screen
  amounts stay identical. WHY: `CardUsed` (= deposit card OR final card) drives the tax-rate
  display, but `Order.CalculateSectionPayment` taxes each portion by *its own* method — so
  picking Card for the deposit advertised 13% while the untouched (null) final method left
  the whole outstanding balance untaxed (entering a 124 price showed a post-tax total of 124 instead of
  140.12). The calculation engine itself was NOT changed.
  - The current-tax row (`*DepositTaxText`) now shows the section's whole tax (`money.Tax`),
    not just the deposit's tax, so it pairs with the post-tax-total line under it. English value of
    `Order.Fields.DepositTax` reworded "Tax on Deposit" → "Current Tax".
  - The final-method **label** deliberately still reads the raw radio selection:
    `FinalBlock` only becomes visible once the deposit is marked received, and by then
    `AutoCompleteSection` has already set that radio explicitly — so it can never
    contradict the displayed money.
  - Gotcha found alongside: the clothing item rows (add / unit price / promo price /
    remove) called only `RefreshClothingTotals()`, so editing a ready-made line item left
    the order grand total + payment summary stale. Any input that feeds a section subtotal
    must go through `RefreshComputedTotals(runAutoComplete: false)` — the section-only
    refresh never reaches `RefreshAllServicesTotalAmount`/`RefreshPaymentSummary`.

- **Cancelled/returned = refunded state**: `Order.IsRefunded` (Status Cancelled/Returned)
  plus `Order.PaymentStatusKind` (`BalanceStatusKind` enum: Outstanding /
  ClearedPickedUp / ClearedNotPickedUp / Refunded) are the single source of truth for
  the balance-status indicator. `OrderPaymentSummaryConverter` "Status" mode maps the
  kind to a label, so the list column, detail panel and receipt all show
  Payment.Status.Refunded (`Payment.Status.Refunded`) for cancelled/returned orders. Main list:
  `IsRefunded` rows are the lightest gray (#C3C9CF / opacity 0.5); `IsPickedUp`
  (completed/shipped) rows stay a bit darker (#9AA3AB / 0.7). Receipt totals colour the
  balance status (green / light green / orange / red via `ReceiptStatusLine` +
  `BalanceStatusBrush`) and OMIT the `Order.Fields.FinalBalance` line when `IsRefunded`. In OrderEditWindow,
  switching the status to Status.Cancelled/Status.Returned dynamically locks every service/payment control
  (incl. OrderEdit.BalanceCleared) via `SetServiceControlsEnabled(false)`, marks all
  checkboxes (incl. OrderEdit.PickedUp) with the `NotApplicableCheckBox` style (red box + red
  strikethrough label + red line across the whole control), and shows the refunded
  balance status; customer fields + the custom-made records list stay usable so
  measurements remain viewable. Reverting the status unlocks and re-runs
  `RefreshComputedTotals`. `_isRefunded` also participates in `RefreshPricingLocks` and
  gates PickedUp enabling. Balance status is computed — no DB change.
  - Gotcha: keep `RefreshPaymentSummary` cognitive complexity ≤15 — the balance-status
    text/colour block was extracted into `UpdateBalanceStatusDisplay`.

- **Custom-service (Custom Service) list flag + measurement printing**: the main list
  dropped the Last Modified column (moved into the detail panel; ordering still
  defaults to LastModifiedDate desc in `LoadOrdersAsync`) and gained a
  **left-aligned** (as of 2026-07-27; originally centered), wrappable **Custom Service**
  column driven by `Converters/CustomMadeServiceFlagConverter`
  (binds the whole `Order`; ConverterParameter `Flag`→`CustomMade.Flag.Yes`/`.No`, `Names`→bracketed
  garment names with a zh 、 / en ", " separator, `NamesVisibility`). Order/Number
  and Balance-Status columns were widened (150→200, 140→180). `Order.
  HasCustomMadeService` `[NotMapped]` (any custom-made record with a garment
  carrying a cm/inch value) gates two new print actions on both the Print toolbar
  submenu and the row context menu: **Print Measurements** (measurements only) and
  **Print Receipt & All Measurements** (receipt + measurements). Both open `Views/
  MeasurementPrintOptionsWindow` (language radios from `AvailableLanguages`
  default=current + unit cm/inch), then print via **PrintDialog + FlowDocument**
  (NOT QuestPDF — this is a print path). `Services/CustomMadeMeasurementReader`
  (static) turns saved records into garment-name lists and per-garment term/value
  sections in the chosen language/unit. Measurement language/unit come from the
  dialog; the receipt portion stays in the UI language; when appended to a
  receipt the measurement block starts on a fresh page (`BreakPageBefore`). No DB
  migration. Build 0 errors/0 warnings.
  - Gotcha: a `bool?`-returning property whose body reads an x:Name control (e.g.
    `IsInch => InchRadio.IsChecked...`) trips SonarLint S2325 ("make static")
    because the generated field lives in the `.g.i.cs` partial the analyzer
    can't see — restructure it as an auto-property set inside the click handler
    instead. Keep per-method cognitive complexity ≤15 (extract inner loops).

- **Measurement Terms system (modular garment measurements)**: predefined,
  localized measurement terms + garments live in `Models/MeasurementTerm.cs`
  and are owned by `Services/MeasurementTermsService.cs` (singleton `Instance`,
  persisted to `measurement-terms.json` under LocalAppData). 21 predefined term
  ids and 7 predefined **locked** garments (jacket/vest/shirt/pants/blouse/
  dress/qipao) seed via `MergePredefined`; users can ALSO add custom garments &
  terms and remap alt-language names (predefined pairs stay locked). Predefined
  names resolve from the `Measure.Term.*` / `Measure.Garment.*` string table;
  custom names from a per-language `Names` dict. Mapping UI = `Views/
  MeasurementTermsWindow` (3-column drag-drop: garments / assigned / all props)
  launched from Local Configuration → Measurement Terms; alt-language popup = `Views/
  MeasurementTermLanguageWindow`. The custom-made window's old static Jacket/Shirt
  grid was replaced by a garment `ToggleButton` selector that renders only the
  related terms as dynamic per-garment cards, backed by a cm/in dual-unit cache
  (`_valueCache`/`_termEditors`); values persist to `CustomMadeServiceRecord.
  Garments` (`GarmentMeasurement`/`MeasurementValue`) on save, legacy Jacket/Shirt
  fields kept for back-compat + seeded for old records; PDF export and the record
  summary converter iterate the selected garments. No DB migration (serialized in
  `Order.CustomMadeRecordsJson`). Build 0 errors/0 warnings.
- **OrderEditWindow payment UI uses a shared card/style system**: `Window.Resources`
  defines `SectionCard` / `SummaryCard` / `SectionHeading` / `PaymentCard` /
  `PaymentTitle` / `StepLabel` / `MethodRadio` / `StepDivider` / `AccentBar`. Each
  service payment sub-card shows an accent-bar header, styled deposit/final method
  labels + radios, and a divider at the top of the `FinalBlock` so the deposit and
  the final balance read as two steps. All `x:Name`/handlers were preserved — restyle only.
- **Currency is a global app setting (not per-order)**: `Services/CurrencySettingService.cs`
  (singleton `Instance`, INotifyPropertyChanged) owns the chosen `CurrencyType` and its
  `Symbol` (￥ for CNY else $), persisted to `currency-setting.json` under LocalAppData.
  Edited via `Views/CurrencySettingWindow` launched from a `Currency Setup` item under Local Configuration.
  `CurrencyAmountConverter` / `OrderPaymentSummaryConverter` / receipt / `OrderEditWindow`
  all read `CurrencySettingService.Instance.Symbol`. The per-order `Orders.CurrencyType`
  column is retained but unused (no migration); the old currency ComboBox + detail row
  were removed. Views refresh on next order load after a currency change.
- **Toolbar → Local Configuration menu**: the standalone Header & Footer button and the three database
  buttons were consolidated into a WPF `Menu` on the `MainWindow` toolbar. Top-level
  `Local Configuration` (`Toolbar.LocalConfig`) auto-expands to `Add or Change Header & Footer`
  (reworded `Toolbar.HeaderFooter`, still → `OnEditBrandingClick`) and a nested
  `Local Database` (`Toolbar.LocalDatabase`) submenu holding Copy Database Path / Reveal Database File /
  Open Data Folder (reused `OnCopyDataPathClick` / `OnRevealDataFileClick` /
  `OnOpenDataFolderClick`). XAML + string-table only; no code-behind changes.
- **Per-portion payment tax (定金/实收定金, 尾款/实收尾款)**: tax now attaches to each
  payment portion only when THAT portion is paid by card (generalizes the old "any card
  taxes the whole section"). Single source of truth: `Order.CalculateSectionPayment(
  subtotal, deposit, ratePercent, downMethod, finalMethod)` → `SectionPayment` struct
  (Subtotal, Deposit, FinalBase=subtotal−deposit, ReceivedDownpayment, FinalCharge,
  Total, Tax); deposit is PRE-TAX and clamped to subtotal. Model section props delegate
  to `AlterationMoney`/`ClothingMoney`/`CustomMadeMoney`; new `Order.ReceivedDownpayment`
  (`Order.Fields.ReceivedDownpayment`); `FinalBalance`/`ReceivedFinalBalance` use the taxed `FinalCharge`; section
  "cleared" = `FinalBase<=0 || manual clear`. `OrderEditWindow` mirrors this via the same
  static calculator (`_alterationMoney` etc.), and its fully-paid / cleared checks compare
  the deposit against the pre-tax subtotal base (NOT the taxed total). Persisted
  `order.TotalAmount` is recomputed on save; legacy mixed-method orders keep their stored
  TotalAmount while the breakdown recomputes. Labels: `Order.Fields.ReceivedDownpayment`
  (实收定金) added; `ReceivedFinalBalance` reworded to 实收尾款.
- **Wording — shop's receiving perspective**: all "paid" money labels now read as
  "received" (customer pays = shop receives). `Order.Fields.PrepaidDownpayment`
  已付定金→已收定金 / "Received Downpayment"; `Order.Fields.PaidTax` 已付税额→已收税额 /
  "Received Tax"; `Order.Fields.PaymentBreakdown` 付款明细→收款明细 (English "Payment
  Breakdown" kept). Neutral 支付方式 (payment-method) labels left unchanged. Key names
  unchanged (display values only).
- **Receipt wording + paid tax + UI polish**: `Order.TotalTax` (`[NotMapped]`, sum of
  the three section taxes) drives a new receipt "Paid Tax" line in `AddReceiptTotals`,
  shown only when `> 0`; the paid final balance is the existing `ReceivedFinalBalance`
  line (kept). Wording: `Order.Fields.FinalBalance` zh 尾款（余额）→剩余尾款 (English
  unchanged); added `Order.Fields.PaidTax` (已付税额 / Paid Tax) to both language blocks.
  Receipt styling (`BuildReceiptDocument`): `ReceiptSectionTitle` enlarged 11→14 (bold);
  new light `ReceiptServiceDivider()` (#E6E6E6, 0.7px) is appended to EVERY service
  section incl. the last; the heavy pre-totals `ReceiptDivider()` and the app-generated
  `Receipt.PrintedAt` line were removed (`Receipt.PrintedAt` key left unused). Removed a
  dead `AlterationTotal <= 0` guard (unreachable given the `AddedToReceipt` guard).
- **Logo placement + full-field receipt + header-driven title**: `ReceiptBrandingSettings`
  now carries a `LogoPlacement` (Left/Center/Right, default Center); the editor exposes
  it as a radio row. `BrandingRenderer.CreateLogoBlock` (FlowDocument) and
  `AlignLogo` (QuestPDF) apply it. The printed receipt now mirrors the main-app detail
  panel — it prints Status, CurrencyType, ServiceType, per-section Tax, PaymentBreakdown
  and Notes (reusing `OrderServicesSummaryConverter` / `OrderPaymentSummaryConverter`).
  The default title (`Main.HeaderTitle` + `Receipt.Title` on the receipt,
  `Customer.Measurements.PrintTitle` on the measurements PDF) is emitted ONLY when the
  header editor is empty (`BrandingRenderer.IsEmpty(branding.HeaderXaml)`).
  `BuildReceiptDocument` is decomposed into per-section helpers for cognitive
  complexity. GOTCHA: SonarLint reports S2325 "make static" for WPF helpers that only
  touch x:Name fields — that's a false positive (making them static breaks the build);
  inline them instead. For `bool?` use `.GetValueOrDefault()` to dodge S1125/S3358.
- **Receipt/measurements branding (header/footer + logo)**: `Toolbar.HeaderFooter`
  button on `MainWindow` opens `Views/ReceiptBrandingWindow` — a rich-text editor
  (B/I/U, font size, align, color swatches) with a logo card and one tab per
  language, each holding a header + footer `RichTextBox`. Persistence:
  `Services/ReceiptBrandingStore` (static) writes `receipt-branding.json` + a
  `logo.*` file under `%LocalAppData%\CameywareOrder\Branding`;
  `ReceiptBrandingSettings` stores per-language `HeaderXaml`/`FooterXaml`.
  `Services/BrandingRenderer` round-trips content via `XamlWriter.Save` /
  `XamlReader.Parse` (FlowDocument ↔ XAML string) and also renders XAML → QuestPDF
  spans for the measurements PDF. Injection points: `MainWindow.InjectReceiptBranding`
  (printed receipt) and `CustomMadeServiceWindow.SaveMeasurementsPdf` (PDF).
  GOTCHA reconfirmed: under ImplicitUsings, QuestPDF + WPF + HotChocolate collide on
  `Path`/`Color`/`FontWeight`/`FontStyle`/`HorizontalAlignment` — alias `Path =
  System.IO.Path` and fully-qualify `System.Windows...`. SonarLint: keep stores
  static (S2325), decompose deep lambdas for cognitive complexity, prefer `IsLoaded`
  over a hand-rolled `_isPopulating` flag.
- **App icon + `Assets/` folder**: source design `Assets/ICONS/app-icon.svg` (white
  clothes hanger on an indigo→violet gradient rounded square); `Assets/ICONS/app-icon.ico`
  is a multi-resolution (16–256) PNG-in-ICO built from a matching GDI+ drawing (no
  SVG rasterizer is installed — `convert.exe` on this box is the NTFS tool, not
  ImageMagick). Wired via `csproj` `<ApplicationIcon>Assets\ICONS\app-icon.ico` +
  `<Resource Include="Assets\ICONS\app-icon.ico" />`; windows reference it with
  `Icon="/Assets/ICONS/app-icon.ico"`. WPF exe icons MUST be `.ico`; SVG can't be
  applied directly.
- **Welcome header image**: `Assets/WELCOME PANEL/welcome_header_enter_system.jpg`
  (tailoring fabric-sample photo) is the language window banner header, registered
  as a `<Resource>`. GOTCHA: WPF stores the resource key URI-escaped + lowercased
  (`assets/welcome%20panel/...`), so XAML must reference it with `%20`:
  `Source="/Assets/WELCOME%20PANEL/welcome_header_enter_system.jpg"`. Confirm
  embedded keys by reading the built dll's `*.g.resources` when a folder has spaces.
- **Language selection window beautified**: `LanguageSelectionWindow` has a photo
  header banner (dark bottom gradient scrim + `LanguageSelection.Welcome` /
  `.WelcomeMessage` text), language options rendered as **radio buttons generated in
  code-behind** from `LocalizationService.AvailableLanguages` (was a ComboBox), and a
  styled full-width "LanguageSelection.Enter" button (`LanguageSelection.Enter`).
  Selecting a radio calls `SetLanguage` immediately so the panel text previews the
  chosen language live.
## Recent decisions / state

- **Custom-made record opens read-only when its section balance is cleared**:
  `OrderEditWindow.OnEditCustomMadeRecordClick` gates on
  `recordReadOnly = _isReadOnly || CustomMadeBalanceClearedCheck.IsChecked is true`
  (the same condition `RefreshPricingLocks` uses to lock the section's pricing).
  When true, `CustomMadeServiceWindow` is opened with `isReadOnly: true`, so its
  existing `ApplyReadOnlyMode` retitles to `OrderEdit.ViewCustomMade`
  ("OrderEdit.ViewCustomMade"), makes every box/radio read-only, hides Save, and — via
  `CanEditDocuments => !_isReadOnly` bound in XAML — disables the document
  upload/replace/delete buttons (the image upload area). The Add-record button is
  already disabled for a cleared section by `RefreshPricingLocks`
  (`RemoveCustomMadeButton`/`AddCustomMadeButton`), so both add and edit respect
  the settled state. Reused the already-proven whole-order read-only path — no new
  keys or model changes.
- **Payment section locks when its balance is cleared**: in `OrderEditWindow`,
  `ApplySectionLock(PaymentSectionControls)` (called from `UpdatePaymentVisibility`
  for all 3 sections) disables the section's deposit-method radios, deposit box,
  deposit-received check, and final-method radios whenever
  `BalanceClearedCheck.IsChecked` is true (or the whole order is read-only). The
  cleared checkbox stays enabled unless the order is read-only, so a section can be
  un-cleared to become editable again. All change paths (manual radio/box edits,
  `AutoCompleteFullyPaidSections`, `ClearAllBalancesCheck`) route through
  `UpdatePaymentVisibility`, so lock/unlock stays consistent everywhere.
  Additionally, `RefreshPricingLocks()` (called at the END of `RefreshComputedTotals`,
  after the `Refresh*Totals` passes that re-enable tax boxes) locks a cleared
  section's PRICING inputs too: price box + tax box read-only, and the item/record
  editors that feed the total (clothing rows + Add Item, Add/Remove Custom-Made).
- **Main window sized to 2K / maximized**: `MainWindow.xaml` sets
  `WindowState="Maximized"` with default `Height=1440 Width=2560` (MinHeight/
  MinWidth unchanged).
- **Right-click record context menu** (Edit / Copy / Delete / Print Receipt) on
  the orders `DataGrid`. Placed on **`DataGrid.ContextMenu`** (NOT in the row
  `Style` `Setter.Value` — `Click` handlers there fail to compile with a
  mis-attributed `MC6007`; see SKILL §11). The row `Style` carries only an
  `EventSetter` for `PreviewMouseRightButtonDown` → `OnOrderRowRightClick`
  (`row.IsSelected = true`) so the shared menu targets the right-clicked row.
  Menu handlers (`OnContextEdit/Copy/Delete/PrintClick`) reuse the existing
  commands/handlers.
- **Copy order** (`MainViewModel.CopyOrderCommand` / `CopyOrderAsync`): loads the
  source `AsNoTracking()` + `Include(Items)`, copies all persisted scalars (no
  `Id`), assigns a new `ORD-{yyyyMMdd-HHmmss}` number + `OrderDate=UtcNow`,
  deep-copies `Items` as new rows, and **resets a closed status**
  (`Completed`/`Cancelled`/`Returned`) → `Processing` (`IsClosedStatus` helper).
  Because the "OrderEdit.PickedUp" tick is derived from `Status == Completed`
  (no own column), resetting the status clears the tick automatically. Saves,
  reloads, re-selects the copy; localized status via `Status.CopySucceeded`.
- **Receipt/detail section gating**: a service section is "added" only when
  `XxxTotal > 0 && XxxDownpaymentMethod is not null`. Exposed as `[NotMapped]`
  `AlterationAddedToReceipt`/`ClothingAddedToReceipt`/`CustomMadeAddedToReceipt`
  on `Order` and reused by **both** `BuildReceiptDocument` (skip section) and the
  `MainWindow.xaml` detail panel (section `Border.Visibility` via the built-in
  `BooleanToVisibilityConverter`, key `BoolToVisibility`). This fixed a bug where
  the Alterations detail section showed whenever `ServiceDetails` was set even at
  price 0 (it had been bound to `ServiceDetails` null-check instead of the gate).
- **Keyboard on orders grid**: `OnOrderRowKeyDown` is a switch — `Enter` opens
  the editor/details, `Delete` runs `DeleteOrderCommand`. Delete confirmation is
  owned by `DeleteOrderAsync` (Yes/No `MessageBox`), so toolbar, context menu,
  and the DEL key all share one dialog.
- New Languages.xml keys (both blocks): `Toolbar.CopyOrder`,
  `Status.CopySucceeded`, `Status.CopyFailed`.

## Recent decisions / state

- **Custom-made mode rename/reorder + section-level tax + input validation**:
  - Modes: Measure Only / Full Custom (Measure Only / Full Custom); **Full Custom is the
    default** (`CustomMadeServiceRecord.ServiceMode` default + editor
    `InitializeMode(... ?? CustomFromScratch)`; radios reordered Full-Custom-first
    with `IsChecked="True"`, container `StackPanel` + radios `VerticalAlignment=Center`).
  - Custom-made **tax moved to section level** like Alterations/Clothing:
    `Order.CustomMadeTaxRate` (`decimal?`) + `CustomMadeSubtotal`;
    `CustomMadeTotal = subtotal + subtotal*rate/100`. Startup column guard
    `("CustomMadeTaxRate","ALTER TABLE Orders ADD COLUMN CustomMadeTaxRate TEXT NULL; ")`.
    `OrderEditWindow` has editable `CustomMadeTaxBox` (default 13%, enabled only
    when a card payment is chosen — `RefreshCustomMadeTotals` mirrors the section
    pattern; persisted in `ApplyPaymentFields`). Per-record Tax/SumTotal removed
    from `CustomMadeServiceWindow`; record `TaxRate` left null (back-compat).
    **Legacy custom-made orders show no section tax until re-saved** (accepted,
    consistent with Alteration/Clothing migration behavior).
  - Accessibility: **Enter opens the editor** — `OnCustomMadeRecordsKeyDown`
    (record list) + `MainWindow.OnOrderRowKeyDown` (orders grid). **ESC closes
    popups** via `IsCancel="True"` on both Cancel buttons. **Deposit boxes clear
    a leading 0 on focus** and restore `"0"` on blur/invalid
    (`RegisterDepositBox` + `OnDepositBoxGotFocus`/`LostFocus`, `_syncingPayment`
    guarded).
  - Validation/formatters (inline red message only where the request asks):
    money regex `^\d*(\.\d{0,2})?$` (`DecimalInputPattern` + money
    `PreviewTextInput`/paste filters); measurements
    `^(\d+(\.\d*)?[+-]?)?$` on the 8 measurement boxes; email `EmailPattern`
    and phone `IsValidPhone` (regex `^\+?[\d\s\-().]+$` + **7–15 digit count**,
    loose common format). `PhoneErrorText`/`EmailErrorText` red inline blocks
    show on `LostFocus` and block save in `TryValidateForSave`. Keys
    `OrderEdit.Validate.EmailInvalid`/`PhoneInvalid` added to both language blocks.
  - **SonarLint S125 gotcha**: explanatory comments containing quoted literals
    or code-like punctuation (`"060"`, trailing `;`, parens) get flagged as
    commented-out code — reword to plain prose.
- Workspace-wide **SonarQube (SonarLint) cleanup** completed and build-verified.
  See SKILL.md §10 for the concrete rule fixes and documented false positives.
- Documented false positives left as-is (do not "fix" by forcing static):
  - `AppDbContext` DbSets (use auto-property form).
  - `AppDbContextFactory.CreateDbContext` (interface impl; `[SuppressMessage]`).
  - `MainViewModel.DatabaseFilePath` (WPF-bound; `[SuppressMessage]`).
- GraphQL server URL composed from `ServerScheme`/`ServerHost`/`ServerPort`
  constants in `App.xaml.cs` (single-const extraction does NOT clear S1075).
- **"OrderEdit.PickedUp" quick-complete** added to `OrderEditWindow`:
  ticking sets status → `Completed` and disables the status dropdown; unticking
  re-enables it; manually selecting `Completed` ticks the box. Guarded by
  `_syncingStatus`. No new DB column (state == `OrderStatus.Completed`).
- **Finalized orders are read-only**: `OrderEditWindow` edit ctor disables
  `FormRoot`, hides `SaveButton`, and shows `ReadOnlyNotice` when the saved
  `Status` is `Completed`/`Cancelled`/`Returned`. Logic inlined in the ctor
  (not a helper) to avoid an S2325 false positive — SonarLint standalone
  analysis can't see XAML-generated fields, so a method touching only
  `FormRoot`/`SaveButton`/`ReadOnlyNotice` looks static to it.
- **Order list status filter**: `MainViewModel.StatusFilter` (`OrderStatus?`,
  null = All) + `StatusFilterOptions`; `RebuildOrdersView` filters by it.
  ComboBox in the `MainWindow.xaml` filter Border reuses
  `OrderStatusToLocalizedTextConverter`, which now returns `Filter.Status.All`
  for a null value.
- **Receipt price detail**: `BuildReceiptDocument` prints per-item unit price +
  line total and per-service `Subtotal` + `Receipt.SectionTotal` for
  Alterations / Ready-made / Custom-made sections.
- **Detail-panel price detail**: the `MainWindow.xaml` right-hand detail panel
  (`Detail.OrderItems`) now shows, per service, a `Subtotal` + section `Total`
  (Alterations & Ready-made) and a per-record price + section `Total`
  (Custom-made). Money via `CurrencyAmountConverter` MultiBinding
  (amount + `SelectedOrder.CurrencyType`); inside item templates the currency
  comes from `RelativeSource AncestorType=Window` `DataContext.SelectedOrder`.
  Labels reuse `Order.Fields.Subtotal` + `Receipt.SectionTotal` (no new keys).
- **Custom-made summary is condensed**: `CustomMadeRecordSummaryConverter`
  (drives the edit-window list, detail panel, and receipt) now shows only the
  garment sections present (`上衣, 衬衫`) instead of every measurement value —
  reads `Customer | Mode | AgeType | <sections>`. Its `SectionName` helper emits
  the localized `Measure.Section.*` label only when that garment has any value.

## Recent decisions / state

- **Alteration service category dropdown**: the Alterations panel's free-text
  "Service Details" box is now a ComboBox (`AlterationCategoryBox`) with two
  options — Garment Adjustments / Others. The selection is stored as a stable
  token (`GarmentAdjustments`/`Others`) in the existing `Order.ServiceDetails`
  column (no new DB column). The detail panel and receipt render the localized
  name via `LocalizationLookupConverter` / `LocalizeWithFallback` with prefix
  `Alteration.Category`; legacy free-text values fall back to the raw string.
  Load/save is inlined (not a helper) to dodge the S2325 XAML-field false positive.
  The dropdown **defaults to the first option** (Garment Adjustments/Garment Adjustments):
  `SelectedIndex = 0` on the new-order path, and edit-load falls back to the
  first item when the stored value matches none (per SKILL.md §5).
- **Order detail panel shows per-section tax**: `Order` has `[NotMapped]`
  `AlterationTax`/`ClothingTax`/`CustomMadeTax` (section Total − Subtotal). The
  `MainWindow.xaml` detail panel adds a Tax row (label `Order.Fields.TaxAmount`)
  in each section, shown only when the amount > 0 via the new
  `PositiveAmountToVisibilityConverter` (`PositiveAmountToVisibility`). Alterations
  and Ready-made place it between Subtotal and section Total; Custom-made (which
  has no Subtotal row) places it before the section Total.
- **Measurement unit toggle (cm/inch) + localized measurement download** in
  `CustomMadeServiceWindow`:
  - Measures section header has `CmRadio` (default) / `InchRadio`
    (GroupName `MeasureUnit`). Toggling converts the 8 measurement boxes via
    `Models/MeasurementUnits` — only the leading number is converted (÷/×2.54,
    rounded to 2), the optional trailing `+`/`-` is preserved
    (`^(\d+(?:\.\d*)?)([+-]?)$`). The conversion lives in that one model, not in
    each caller; the editor, the printed sheet and the PDF each had a copy and the
    print path drifted out of step. **Storage stays
    canonical cm**: `MeasurementForStorage` converts inch→cm on save so the
    receipt/summary/detail panels (which read the raw strings) never see inch.
  - A "Download Measurement" section under Custom Price has language radios
    (`DownloadChineseRadio` default / `DownloadEnglishRadio`) + a submit button
    (`OnDownloadSubmitClick`). The old footer Download button was removed.
    The PDF is generated in the **selected** language (not the UI language) via
    `LocalizationService.GetText(key, languageCode)` — a per-language lookup that
    avoids `SetLanguage` UI flicker — and includes a Unit info row. Box values
    already hold the selected unit, so the download matches cm/inch naturally.
  - `BuildPdfFileName(langCode)`: sanitize invalid chars→`_`, collapse
    whitespace/underscore runs to a single `_` (`Regex.Replace(@"[\s_]+","_")`),
    then append `_zh` / `_en` (`ShortLanguageName`).
  - Gotcha: `Path` is ambiguous (`HotChocolate.Path` vs `System.IO.Path`) in
    this file — fully qualify `System.IO.Path`.

## QuestPDF — page structure and print verification

- **`page.Header()` / `page.Footer()` repeat on every page; `page.Content()` does
  not.** A letterhead composed into Content renders once and stops, so a sheet
  that runs to a second page carries branding on page one only and drops its
  footer wherever the last block happened to end. This is invisible on any
  one-page test document, which is why it survived so long in the measurements
  export.
- **Wrapping a heading and its table in a single `column.Item().Column(...)` does
  NOT make them atomic.** A Column splits across pages like anything else, so the
  heading can still end a page with its rows orphaned overleaf. For a repeating
  section label use `table.Header(...)` — QuestPDF re-draws a table header on
  every page the table spans, which also answers "what are these numbers?" on the
  continuation page.
- **`BrandingRenderer` parses branding with `XamlReader.Parse(xaml) as
  FlowDocument`.** Any other root element (a `Section`, say) casts to null and
  renders **nothing at all**, with no error. Header/footer XAML must have a
  `FlowDocument` root.
- Creating a FlowDocument from XAML needs an **STA** thread — a console harness
  must mark `Main` with `[STAThread]` or the parse fails silently into the same
  empty result.

### Verifying a print layout by rendering it

`IDocument.GenerateImages(...)` rasterises pages, which is the only way to check a
property like "the header repeats" — reading the composition code cannot tell you.
Two rules learned the hard way here:

- **Locate bands by landmarks the layout actually draws, not by fractions of page
  height.** Bands guessed as "the top 10%" ran past the letterhead into the
  content area and reported that the header differed between pages when what
  differed was the measurements underneath it. Find the rule (a row inked across
  >75% of the width) and compare everything above it.
- **Never compare pixels across content at different offsets.** A taller header
  shifts the body by a fractional number of pixels, so identical content
  rasterises to different bytes — the comparison then tests the anti-aliaser.
  Compare a measured *structural* quantity instead (body height, rule position).
  Byte-identical comparison is still the right tool for the same band on
  different pages, where nothing has moved.

## Driving App's own startup/sign-in flow in a harness

`App`'s private flow methods can be exercised without running `OnStartup`: create the
instance with `RuntimeHelpers.GetUninitializedObject(typeof(App))`, set the `_host`
field by reflection to a host that registers `AppDbContext`, and invoke the method.

Modal dialogs are answered by registering a class handler for `Window.LoadedEvent`
(`EventManager.RegisterClassHandler`) and setting `DialogResult` there — every
`ShowDialog()` then returns without a human. Recording the window types as they load
gives the flow's actual sequence to assert on ("picker → login → picker"), which is
what makes a navigation change testable at all rather than only reviewable.

Any harness that opens a window needs `<Resource Include="Assets\ICONS\app-icon.ico" />`
and a copy of the file: the windows set `Icon` with a RELATIVE pack URI, resolved
against the entry assembly, which is the harness rather than CameywareOrder.

## Simulating keyboard input in a harness

**`InputManager.ProcessInput` with a fabricated `KeyEventArgs` does not work here.**
It needs the keyboard device bound to a real foreground window, which a harness
cannot guarantee, so events are discarded silently. Symptoms seen: the first
assertion failing while every later one passed, and on a re-run every key vanishing.

The dangerous part is not the flakiness. Assertions of the form "this key must NOT
do anything" **pass when the input is discarded** — a green light for the absence of
behaviour. If a negative assertion cannot fail, delete it rather than keep it.

Use `target.RaiseEvent(new KeyEventArgs(...) { RoutedEvent = UIElement.PreviewKeyDownEvent })`
instead. It is deterministic and still exercises the real tunnelling route, which is
what a window-level shortcut depends on. What it cannot do is fake
`Keyboard.Modifiers` — that reflects the physical device, so modifier guards are not
testable in-process and should be reviewed by eye instead of pretend-tested.

## Window-wide keyboard shortcuts

Use a `PreviewKeyDown` handler on the Window, **not** an `InputBinding`/`KeyBinding`,
for any shortcut on a key that ordinary controls use. An InputBinding fires no matter
what has focus, so an arrow shortcut would fire while the user moves the caret in a
text box. Walk UP the tree from `Keyboard.FocusedElement` when deciding to stand down
(`TextBoxBase`, `PasswordBox`, `ComboBox`, `DatePicker`, `Slider`, `MenuBase`) —
focus lands on a part inside those controls, so testing the focused element alone
misses it. Stand down whenever any modifier is held; Alt+Left and Ctrl+Left already
mean something.

When a shortcut changes what is displayed, move focus to the new content and mark the
status text as a live region (`AutomationProperties.LiveSetting`, plus an explicit
`RaiseAutomationEvent(AutomationEvents.LiveRegionChanged)` — rebinding the text does
not raise it). Otherwise the shortcut is unusable by exactly the people it was added
for.

## Which assembly the harnesses actually compile against

**Every harness now references `c:\Projects\CameywareOrder\bin\Debug\net8.0-windows\
CameywareOrder.dll`.** Fourteen of them used to point at
`scratchpad/navswap/bin/CameywareOrder.dll` instead; they were repointed on
2026-07-28.

That split existed because the app locks its own output while running, so builds get
redirected with `-p:OutputPath=<scratch>`. A redirected build leaves the project's
`bin/` untouched, and any harness pointing there silently tests **old code** —
compiling and passing, which is the worst possible failure mode. It cost a full round
of "why does this method not exist", and a green suite once reported against code that
had never been built.

**Build to the normal output path.** Kill the app first
(`tasklist //FI "IMAGENAME eq CameywareOrder.exe"`) rather than redirecting around it.
If a redirected build ever is unavoidable, EVERY harness result from it is unverified,
not just some.

`scratchpad/run-suite.ps1` runs the whole set and prints one line each. Use it rather
than picking harnesses by hand — `uicheck` sat red for a week because it was not being
run, and a partial suite reads exactly like a green one.

## Enumerating languages

Never enumerate the installed languages in code — no radio per language, no
`code == "zh-CN" ? … : …`. Build every language list from
`LocalizationService.AvailableLanguages`, which is discovered from
`Settings/System/Languages`. Label each option with the language's OWN name from its
own file (`LanguageOption.Name`) rather than a per-language string-table entry, so a
new language names itself and adding one stays "drop a file in".

Two fixed radios in the download-measurement section meant fr-FR shipped as a full
system language whose measurements could not be exported, while the print dialog
right beside it was already dynamic. When asserting a "supports N languages" claim,
first assert the install HAS more than two — otherwise the check passes vacuously.

### Adding a language is a DATA task as much as a file task (2026-07-29, es-ES)

Adding Spanish needed no `.cs`, no `.xaml` and no `.csproj` edit — the claim holds.
What it did need was data, and none of it is obvious from the code:

- **Every existing shop has to be told it installs the new language**, or the shop
  that used to install "all of them" now installs three of four and the toggle in it
  silently offers less than it did. `Shop.InstalledLanguagesJson` stores an explicit
  list, so "all" was never a value — it was a snapshot.
- **Every existing shop is nameless in the new language.** `Shop.ResolveName` falls
  back to `values.Values.FirstOrDefault(…)` — *dictionary insertion order*, not
  English — so a shop whose first stored name was Chinese showed a Chinese name to a
  Spanish reader. Working as documented ("any other language that has one"), and
  invisible until a language is added. Fill the gap in the seed data; do not
  re-order the fallback, which would change what every other language falls back to.
- **A hard-coded language COUNT in a harness is the coupling the split removed.**
  `formatcheck` asserted `AvailableLanguages.Count == 3`, so the fourth language
  failed a test that had nothing to say about it. Counts now come from the folder,
  and the per-language sweeps (nothing falls back to English, placeholders agree)
  iterate `AvailableLanguages` instead of a written list.
- **A genuine cognate is not an untranslated string.** Spanish spells `Branding.Color`
  "Color" and `Order.Fields.Subtotal` "Subtotal". The fallback sweep flags them, and
  the fix is a `(key, language)` exemption — NOT the shared-across-all-languages list,
  which would also stop anyone noticing the same key left untranslated in French, and
  not padding the Spanish out ("Color del texto") to make a test pass.

## Harnesses that read live user data

`authcheck` runs against the real `credentials.json` and had rotted in two ways at
once, both of which read like authorization regressions and were neither:

- It asserted "a Manager in **every** existing shop" against an already-migrated
  file. Two shops were created after that migration ran, and a new shop correctly
  grants nobody access. The fixture, not the assertion, was stale — it now rewinds
  the file to version 1 first so the migration has something to migrate.
- It signed in as `staff`/`staff`, and that password had since been changed in the
  application. It now pins the fixture passwords via `SetPassword` during setup —
  including `admin`, whose password had ALSO drifted. `SetPassword` is gated by its
  callers rather than by the service, so it can be called before signing in, which
  is what makes pinning the administrator possible at all.

The general rule: a harness reading live data must **establish** the state it
asserts on, not assume the state it found the day it was written.

The same trap has a THIRD form, found when tax jurisdictions shipped: the shop the fixture happens to
open is ambient state too. `balancecheck` opens the first shop by id, that shop acquired a
tax-inclusive location from a one-shot backfill, and two assertions went red over a `13m` literal
standing in for "the card surcharge". It now derives both the mode and the rate from the shop
(`TaxJurisdictions.For(...)`, `PaymentTaxRules.Active.RateFor(...)`). Currency was the first thing
this happened with, tax treatment the second — assume any shop-level setting is next.

A FIFTH form, and the one that let a bug reach the user: **a test must be able to tell a value in USE
from a value being discussed.** A `storecheck` assertion checked that the abandoned low-contrast colour was
gone by searching the XAML for `#FECACA` — and failed on the COMMENT explaining why it had been abandoned.
Matched as `Foreground="#FECACA"` it says what it means. The general shape: when asserting the ABSENCE of
something in source text, match it as the syntax that would make it live, never as a bare substring, or the
documentation gets reported as the defect.

And a FOURTH, the same day: **searching live data for a fixture is assuming, not establishing.**
`currencycheck` proved its currency backfill against `SELECT Id FROM Shops WHERE CurrencyType <> 1`
and found a real JPY shop for weeks. The moment that shop was switched to CAD in the application,
three assertions went red and read like a regression in a backfill nobody had touched. A query that
happens to return a row today is a hard-coded expectation with extra steps: the fixture now *makes*
the shop non-CAD (`UPDATE Shops SET CurrencyType = …`), and picks the shop with the most orders so the
"orders start out wrongly marked" precondition cannot be vacuous either.

**And the failure mode to fear is the QUIET one.** `taxcheck` — written the same day, by someone who
had just recorded the rule above — guarded its yen assertion with `if (jpyShops > 0)`. When the same
shop changed currency it did not go red: it silently skipped, the harness stayed green, and the tally
dropped 351 → 350 with nothing pointing at it. Only a remembered number caught it. **A conditional
assertion is a coverage hole with a timer on it.** If a precondition might not hold, establish it and
assert it; never wrap the assertion in the condition. The same principle as "no silent caps" — a
harness that quietly covers less is worse than one that fails, because failure is self-reporting.

## A checkbox the user cannot untick (2026-07-29)

- **A control's state and the fact it describes are different questions.** The "clear all
  final balances" master was driven by `IsOrderBalanceCleared()` — "is anything owed" —
  which is TRUE for a section whose deposit already covers its total, whatever the user has
  ticked. So the box sprang back on the moment anything recomputed and a fully-deposited
  order could never be reopened. Drive a checkbox from what the user has MARKED; keep the
  derived money fact for the money.
- **A convenience must not re-assert itself on every pass.** `AutoCompleteSection`
  re-evaluated its condition on each refresh, so it re-ticked what the user had just
  unticked. Auto-behaviour belongs on the TRANSITION into a state (`if (!wasAutoCompleted)`),
  not on every evaluation of it — otherwise the user cannot win the argument, and the flag
  that was supposed to remember the state is only remembering that the rule still applies.
- **Fix the control, not the money model.** `Order.IsSectionCleared`'s `FinalBase <= 0` rule
  feeds `FinalBalance`, the receipt and the list column. Changing it to satisfy a UI
  complaint would have re-priced history; changing only what the checkbox reads did not.
- **A "due / received" pair must hide the received half until it is true.** Showing it from
  the start states money was taken when it was not; showing a zero is worse — it cannot be
  told apart from a portion that was genuinely free. Hide the label WITH the value: a lone
  label reads as a value that failed to load.
- **Harness: the snap-back was one recompute away.** Asserting immediately after the click
  passed while the bug was fully present. Any "does it stay?" claim has to force the pass
  that used to undo it.
- **Harness: a new order opens with the alteration category on "None"**, which switches the
  service off, so the price box is ignored and every figure reads zero. Select a real
  category before pricing anything.

## Recording who did something (2026-07-29)

- **Store the RENDERED name, not a key, for anything that gets printed.** `LastModifiedBy`
  holds the crew member's display name as it read when they saved. Resolving it at print
  time would change what an old receipt says the day somebody is renamed and blank it the
  day they are deleted — and accounts live in `credentials.json`, outside the database, so
  there is no key to point at anyway. An audit line is a snapshot, not a join.
- **Take it from the SESSION, never from the form.** "Who saved this" must not be a field
  anybody can type into. Stamp it beside the timestamp in the save path, and leave it alone
  when nobody is signed in so a save can never blank a real name.
- **Omit an audit line rather than printing it empty.** Every row that predates the column
  has no name; a label with nothing beside it reads as a printing fault, not as an absence.
  Hide the LABEL with the value — wrap the pair in a panel and bind that panel's visibility.
  Hiding only the value leaves a heading over nothing, which is worse than either.
- **`IsVisible`, not `Visibility`, when a test asks "is this hidden".** A child of a Collapsed
  panel still reports `Visibility.Visible` for itself, so checking the element alone reports a
  hidden row as shown — exactly inverting the claim being made.

## Adding a column to the model breaks harnesses in two different ways (2026-07-29)

Adding `Orders.LastModifiedBy` failed four harnesses at once with "no such column". Two
distinct causes, and the second is the one that recurs:

- **Harnesses that read the LIVE database inherit whatever schema it has**, and the guards
  only run when the app starts. After adding a column, run them against the live file
  (`scratchpad/livemigrate` does it by reflection, never a copied ALTER) — that is exactly
  what the user's next launch does, so it is simulation rather than fudging.
- **A fixture that "migrates itself" must run EVERY guard.** `headercheck` called
  `EnsureShopSchemaAsync` alone, covering Shops and not Orders. It had already been fixed
  once for this precise symptom when a Shops column was added; the first ORDERS column added
  afterwards broke it again identically. A half-migration looks exactly like a regression,
  and costs the same diagnosis every time. The app runs both at startup; so must the fixture.

## Never widen the money rule through an OPTIONAL parameter (2026-07-29)

`CalculateSectionPayment` exists so the model and the live editor cannot diverge. Adding
tax-inclusive pricing threaded a new `bool pricesIncludeTax = false` onto it — and the default
turned a *compile error at every unconverted call site* into **silence**. Two balancecheck
assertions went red: the harness recomputes its expected figures by calling the rule directly, kept
the 6-argument form, and so kept exclusive arithmetic while the window it measures had switched to
inclusive. Nothing failed to build; the numbers simply stopped agreeing.

The default parameter looks like backward compatibility and is the opposite of it: the one thing the
single-source rule guarantees is that everybody gets the SAME answer, and an optional argument
guarantees that whoever forgets it gets a different one. It is now **required**, which is what listed
the call sites. **And check the presentation layer, not only the arithmetic:** every breakdown here
derived tax as `Received − Deposit`, which is structurally zero once tax is embedded, so the receipt
printed "Tax 0" beside a non-zero "Tax paid". A second pricing mode is not one branch in one
function; it is a branch in every place that *explains* the number — which is why the per-portion
split and the mode itself now travel ON `SectionPayment` (`DepositTax`, `FinalTax`,
`PricesIncludeTax`, `DepositStageTotal`) instead of being re-inferred downstream. Carrying the answer
is cheaper than trusting five call sites to re-derive it the same way.

## A setting whose value the UI hides has no way to be right (2026-07-29)

The store-location picker hides the per-method tax matrix for an inclusive jurisdiction — correct
reasoning (there is nothing per method to decide when tax is embedded in the price) applied to the
wrong thing: the matrix is still where the RATE comes from. So an inclusive shop's tax is computed
from a field it cannot see, and the jurisdiction's own `standardRatePercent` — the number the shipped
presets exist to carry — is read at exactly one guarded call site and never for an inclusive
location. Live data confirmed it: a shop located in `JP` carrying 13% on every method.

Two rules fall out. **When you hide an input, move its value to the thing that replaces it** — do
not leave the hidden control as the source of truth. The fix was to take the inclusive rate from the
jurisdiction (`TaxJurisdictions.IncludedTaxRatePercent`), apply it to both portions, ignore the
per-method rules entirely, and STATE the rate on screen where the matrix used to be. And **a shipped
preset that is never read is indistinguishable from a wrong one**: grep every field of a new data
file for a real consumer before believing the file is wired up — one guarded call site that the
guard's own condition excludes reads, in a diff, exactly like a wired-up field.

## Grep for the concept before adding the column (2026-07-30)

Store Management needed "delist a shop". I designed `Shop.DelistedOnUtc`, wrote the migration guard, the
CREATE TABLE entry and the service methods — and only found `Shop.IsArchived` when I opened the picker to
filter delisted shops out and saw it was **already filtering on exactly that**. It had shipped with the
comment "hidden from the shop picker without deleting its orders", was honoured in three places, and had
no UI anywhere to set it. What the feature was missing was not the concept; it was the screen.

Two ways this bites, and the second is the expensive one:

- **A duplicate flag means two answers to one question and no rule about which wins.** Corrected so
  `IsArchived` stays authoritative, `IsDelisted` delegates to it, and the new timestamp is an audit stamp
  beside it — not a second opinion. Delisting then took effect in all three existing call sites for free.
- **A column nothing writes reads exactly like a column that does not exist.** `IsArchived` had been a
  landmine in the sense this file already records; the fix for that class of thing is a UI or a deletion,
  never a second column that means the same.

Cheap habit that would have caught it: before adding a persisted property, grep the MODEL for the
concept's synonyms (`archive`, `delist`, `disable`, `active`, `hidden`, `retired`), not just for the name
you have in mind.

## A central SaveChanges stamper is a trap for importers (2026-07-30)

`AppDbContext.StampNewOrdersWithShop` sets `ShopId`, `CurrencyType` and `PricesIncludeTax` on every ADDED
order from the OPEN shop. That is right for every call site that creates an order — it exists precisely so
none of them can forget — and silently catastrophic for one that restores orders from an archive, where all
three are facts recorded when the order was taken, possibly on another machine. Unguarded, a restore
re-parents every order to whatever shop happens to be open and re-denominates its money; nothing fails,
and the damage surfaces the next time somebody reprints a receipt.

`SuppressShopStamping()` is an explicit `using` scope, made deliberately awkward to reach: a constructor
flag or a setter would invite the next caller to switch off the invariant for convenience. The rule
generalises — **any centrally-enforced "you cannot forget this" stamp needs one documented escape for the
caller who is more authoritative than the ambient state, and exactly one.**

## A theme trigger with TargetName beats your local value (2026-07-30)

The delete-confirmation phrase was near-white on a near-black panel — about 17:1 — and rendered as
near-white on light grey. Cause: the box derived from `ThemedTextBox`, whose `IsReadOnly` trigger repaints
`Chrome.Background` to the disabled grey **through `TargetName`**. A `TargetName` setter writes the
template child's property directly, so it beats the `TemplateBinding` that a local
`Background="Transparent"` on the control feeds. The local value is not ignored — it simply is not what
paints that pixel.

So a control whose *state* the theme styles opinionatedly (read-only, disabled) cannot be recoloured by
setting its own Background; it needs its own template. Two related notes from the same fix:

- **A read-only `TextBox`, not a `TextBlock`,** wherever text must be selectable — WPF `TextBlock` cannot
  be selected at all.
- **Selection has to be visible against the background you chose.** The theme's indigo selection on
  near-black is invisible, so the phrase read as uncopyable even though selection worked. "Reads as
  uncopyable" and "is uncopyable" are the same defect from where the user sits — set `SelectionBrush`
  explicitly, and give it a Copy button.
- **Assert contrast as a NUMBER.** Contrast is invisible in a diff and nobody re-checks it. `storecheck`
  computes real WCAG ratios from the colours *read out of the shipped XAML*, so editing the window moves
  the test; a copied constant would keep passing after the screen changed.

## A check narrower than the rule it checks is worse than no check (2026-07-30)

The English-only rule covers source **and Markdown** — it says so explicitly. The verification grep
added to enforce it globbed `*.cs,*.xaml`. So for weeks the command reported *clean* while the
companions eroded to roughly **310 lines** of Chinese UI labels across `Architecture.md`, `context.md`,
`TODO.md` and `RefinedTODO.md`. Every run of the check made the situation look better than it was:
"unchecked" is honest, "checked and clean" is a false negative that stops anyone looking. **Scope the
verification to the rule, not to the files you were thinking about when you wrote it.**

Three things learned doing the sweep, all of which cost a wrong attempt first:

- **It has to be a whitelist of known labels, never a CJK strip.** A bare-token pass produced
  half-English wreckage — `Order.Fields.FinalBalanceShort结清` — because short tokens (`定金`, `尾款`,
  `税率`) are substrings of compounds the list did not enumerate. Longest-first ordering is necessary
  but not sufficient; the map must be *complete* over its own tokens.
- **Most of the hits were sanctioned, and a regex cannot tell.** Sort every hit by what the Chinese is
  DOING: naming a UI surface (violation → use the key), naming a string-table VALUE (the
  `` `Key` (value) `` form, a rename record like `已付定金→已收定金`, a line of rendered output → keep,
  it *is* the data), or quoting the user verbatim (keep). Preview the transformation on real lines
  before applying it — that is what surfaced the value-rename records, which a blind pass would have
  destroyed, taking the meaning with them.
- **Verbatim quotes are not only on `- Ask:` lines.** They turn up mid-Notes and in `- Why:` too, so
  protect by looking for the quote character rather than by line prefix. And a region-based guard needs
  a terminator that cannot fail to arrive: an unclosed quote left mine protecting the rest of the file.

Mechanics worth reusing: the mapping lives in a **UTF-8 JSON side-file** read with an explicit
encoding, because PowerShell 5.1 decodes a BOM-less `.ps1` as ANSI and would mojibake any non-ASCII
literal typed into the script (see the encoding note earlier in this file). Back the docs up first —
this rewrites files the project reads every session.

## A refused save needs three surfaces, and one code path (2026-07-30)

`OrderEditWindow` had eleven validation checks and no rule behind how any of them reported: five raised
a `MessageBox`, two wrote a message under their field, and every one set an `ErrorText` that sits at the
FOOT of a form taller than the window — where the eye that just clicked Save never goes. The customer
name got none of the three.

A refused save has to answer three different questions, and each surface answers one:

- **the dialog** — something is wrong NOW (unmissable, which matters when the button is off-screen);
- **a banner above the form, outside the ScrollViewer** — WHAT is wrong, all of it;
- **a message under each input** — WHERE.

Getting them consistent is a matter of one path, not of discipline: `Fail(key, inline, focus)` and
`TryRequireFilled(fields)` are the only things that report, so a new check cannot forget a surface.

Three details that are the actual work:

- **Collect, don't fail fast.** The ask was "the customer name AND the mobile number" — two facts.
  Fail-fast can only ever name the first, so a user clearing both learns the second rule only after
  fixing the first. Required-empty fields are gathered in one pass, all marked, all listed.
- **Clear on the way in, and clear as the user types.** A field corrected between two attempts keeps
  its red line otherwise, which is worse than never having shown one: they did what they were told and
  the form still accuses them. Typing clears only — it does not re-validate, so nothing turns red while
  somebody is halfway through an address.
- **A message must not outlive its control.** The cancel/return reason row is collapsed unless the
  status is Cancelled/Returned, so its messages are cleared when the row hides — red text under a
  control that is no longer there describes a rule that no longer applies.

**And keep the dialog in ONE wrapper.** `TryValidateForSave` shows it; `ValidateForSave` marks and
returns. That is not tidiness — a `MessageBox` reached from inside a check blocks the thread, so a
harness driving Save with a blank field hangs on a dialog nothing can answer (the same trap as the
reseed confirmation). The seam is what makes validation testable at all, and it also buys one dialog
listing every problem instead of one per field.

## A control the UI never shows cannot govern the value it owns (2026-07-29)

`ShopLocalizationWindow` seeds one currency row per currency the **system's** languages offer, plus
whatever the shop already accepted; the right-hand cards are grouped by the languages the **shop**
runs in. Those two sets are not the same, and nothing reconciled them. A shop holding `["CAD","JPY"]`
with `["en-US","fr-FR"]` ticked therefore had a JPY row that was ticked, invisible, offered in the
preferred-currency picker, and written back on save — with no tick box anywhere on screen to remove
it.

The rule: **a panel must return exactly what it shows.** `TickedCurrencies()` is now scoped to what the
ticked languages bring, which is precisely what the cards display, so the record and the screen cannot
disagree. Note what this replaced: a deliberate rule that kept such a currency so a branch would not
"silently stop taking money it had said it takes". The *intent* was right and the mechanism was wrong —
an invisible tick cannot be reviewed, confirmed or withdrawn, so it preserved the value by making it
unmanageable. Where a value must survive, give it a control; where it cannot have one, let it go and
guard the floor instead.

Two things that made this hard to see, both worth copying:

- The bug was **only** visible in real data. Every fixture ticked the language that brought the
  currency, so the two sets coincided and the defect could not appear. The regression test now uses
  the reported shop's exact stored state as its fixture.
- The stored record is the evidence. `SupportedCurrenciesJson` vs `InstalledLanguagesJson`, read
  straight out of the live database, turned "the dropdown looks wrong" into a one-line diagnosis. Probe
  the data before reasoning about the screen.

**The floor guard belongs live, not on OK.** "At least one currency" was already checked in the Done
handler, which is a refusal — the user clears the last tick, sees nothing, and is told only when they
try to leave. It now repairs on every toggle *and on the way in*: red inline text naming the rule, and
the first offered currency re-ticked. A tick that springs back is normally a defect (see "a checkbox
the user cannot untick"); the difference is the message beside it. Springing back silently reads as a
broken checkbox, springing back next to a red line reads as the rule. Repairing on the way in matters
too, because a shop can arrive already invalid — and the red line is what stops that repair being a
silent rewrite of its record.

## One market's paperwork must not live in the shared string table (2026-07-29)

`"GST/HST"` was spelled into **fifteen** string-table values — the Shop Settings field label, the
branding card title, the receipt line, in all five languages. So a shop in Osaka read
`税番号（GST/HST）` in its own settings and printed `GST/HST 番号：…` on its own tax slip. The
translations were all correct; the *fact* was Canadian.

The same shape as the rate hard-coded into jurisdiction display names, and the same fix: the
jurisdiction declares which number it issues (`TaxNumberLabel` → a `TaxNumber.<name>` key), grouped by
tax REGIME rather than by jurisdiction, because that is the real relationship — Ontario, Alberta and
BC share one GST/HST number; France and Spain each issue an EU VAT number. Adding a market then costs
one line of JSON plus one label per language, and never touches the code.

Three things that fall out and are easy to get wrong:

- **Do not infer "issues a tax number" from the pricing mode.** Canada's GST/HST *is* a consumption
  tax, and Canada quotes prices tax-EXCLUSIVE — so the two questions have different answers, and
  inferring one from the other silently drops the number from the home market. Declare it.
- **Keep a generic fallback.** A shop that relocates somewhere issuing none still has a number
  stored, and a receipt that drops it — or prints it unlabelled — is worse than one that calls it
  "Tax number". Hide the INPUT, never the stored value.
- **A hidden `TextBox` keeps its text in WPF**, so collapsing the field cannot wipe what is in it on
  save. That is what makes hiding safe rather than destructive; it is asserted, not assumed.

And where a printed document may be rendered in a language other than the UI's, the jurisdiction has
to expose the KEY (`TaxNumberKey`) and not only the resolved string — `ShopLetterhead` prints a
measurement sheet in the customer's language, not the operator's.

## A confirmation prompt inside an event handler hangs every harness (2026-07-29)

Adding "ask before this discards the configured tax matrix" put a `MessageBox.Show` inside
`SelectionChanged`. It blocks the thread that raised the event, so the harness that sets
`LocationBox.SelectedValue` never returns from the assignment — and the failure mode is the
expensive part: **it looks like a slow test, not a stuck one.** The suite sat at 0/0 for that harness
with the process `Responding=True` and low CPU, which reads exactly like heavy I/O. What proved it was
enumerating the process's top-level windows and finding `class=#32770` — a dialog class — titled with
the confirmation's own text:

```powershell
# EnumWindows filtered to the harness pid; #32770 is the Win32 dialog class
[WinEnum]::For([uint32](Get-Process taxcheck).Id)
```

The fix is a seam, not a flag: split the **question** from the **asking**. A pure
`WouldDiscardConfiguredRules(jurisdiction)` predicate is asserted directly, in both directions, while
`ConfirmReseed` stays a one-liner that only reaches the dialog when a person is there to answer it.
Then arrange the harness's *other* path so the prompt cannot trigger at all — start from rules the
switch would not change — rather than hoping it does not. Any `MessageBox`, `ShowDialog`, or
`PrintDialog` on a path a harness drives needs the same treatment.

One assertion written for this was simply wrong and is worth keeping as a caution: "re-picking the
location it already has asks nothing" failed, and the code was right — the predicate is about whether
the ROWS would change, not about which code is selected, so a hand-tuned matrix would be flattened by
its own location's seed too. When a new assertion fails, decide which of the two is wrong before
touching either.

## Currencies derived from languages (2026-07-29)

- **Put the language→currency mapping IN the language file, not in code.** Each
  `*.lang.xml` declares `Currency.Codes` (`CAD,USD` for en-US, `CNY` for zh-CN, …), so
  "adding a language is dropping a file in" covers its currency too, and a special case
  like "English shows both CAD and USD" is a value rather than a branch. A build that
  ships en-CA instead needs no code change.
- **A derived set still needs a bound.** `CurrencyType` is persisted as INTEGERS on two
  tables, so the enum decides what can be STORED even though the languages decide what is
  OFFERED. A declared code the enum cannot name is dropped, never guessed at — inventing a
  currency would put an amount on a receipt in money the system cannot record.
- **Not every currency has two decimal places.** JPY has no minor unit; `¥1,695.00` is
  wrong in the same way the wrong symbol is. Formatting owns symbol AND digits together
  (`CurrencySettingService.Format`), because the two are one fact about a currency and
  splitting them is how `{symbol}{x:N2}` ends up hand-written at four call sites.
- **An unresolved string key renders AS the key.** Adding enum members without their
  string-table entries put "CurrencyType.EUR" on screen — it reads as a broken control, not
  a missing translation, so nobody files it as a localization bug. Assert that every enum
  member resolves to something other than its own key.
- **One fact, one row object.** When the same currency is reachable from two places in a UI
  (EUR under both Français and Español), share the row rather than duplicating it. Two
  independent tick boxes for one currency can disagree, and then there is no answer to
  "does this shop take euros".
- **`ComboBoxItem`s added to `Items` in a constructor log binding errors.** Built before the
  ComboBox is in a visual tree, the stock template's `RelativeSource FindAncestor`
  alignment bindings have no `ItemsControl` to resolve against — four errors per picker,
  invisible unless something counts them. Use `ItemsSource` + `DisplayMemberPath` and let
  the ComboBox generate its own containers.

## Per-order currency (2026-07-29)

- **A shop's setting describes TODAY; an order's column describes when it was priced.**
  Money on screen must come from `order.CurrencyType`, never from
  `CurrencySettingService.Instance`. Reading the shop reprints a ￥1,695 order as
  "$1,695.00" the moment the branch starts taking dollars — not a display bug, a wrong
  amount on a document a customer keeps. `ShopCurrencies.SymbolOf(order)` is the one way
  to ask.
- **A column that is never written is not a spare column, it is a landmine.**
  `Order.CurrencyType` existed for months, was read by nothing and written by nothing, so
  every row held the enum default (CAD) regardless of its shop — including all 44 orders
  in the CNY shop. It looked harmless right up until display started trusting it. Any
  feature that starts honouring a dormant column needs a backfill in the same change.
- **Pin a one-shot data repair to the arrival of the column that motivated it**, not to
  startup. The backfill is safe only because the column was never written — "CAD" could
  not mean anything but "unset". That stops being true the instant the editor starts
  saving it, so re-running it later would destroy real choices.
- **Store an enum in JSON by NAME, never by its integer.** The numbers are an
  implementation detail; reordering the enum would silently re-denominate every shop.
  Names that no longer resolve are dropped rather than guessed — every guess about money
  is a wrong amount on somebody's receipt.
- **A "supported set" feature is not automatically the language feature again.** Language
  is how a screen reads and an administrator may see all of them; currency is a fact about
  the order, so there is no per-user override — pricing outside the shop's set would be a
  real, wrong number. Copy the SHAPE (`Supported`/`Preferred`/`CanChoose`, never-empty
  fallback, tick list + picker containing only what is ticked), not the semantics.
- **Check whether the plumbing already exists before building it.** Every
  `CurrencyAmountConverter` binding already passed the order's currency as `values[1]`; the
  converter discarded it. The whole list and detail panel were one line.

## Fitting windows to the screen (2026-07-29)

- **A window's `MinHeight` is a FLOOR that WPF honours against the desktop, not a
  preference.** `OrderEditWindow` declared `MinHeight="900"` while a common laptop offers
  a 752-tall work area, so the bottom 148px — the pinned Cancel/Save footer — sat below
  the screen and **could not be dragged into view**. The layout was never wrong: it is
  `Auto` title / `*` ScrollViewer / `Auto` footer, exactly right. The window simply
  asserted a minimum bigger than the display. Check every `MinHeight` against 728 (a
  1366×768 laptop) before adding one.
- **`LayoutTransform`, never `RenderTransform`, for fitting.** Only a layout transform
  makes the content MEASURE smaller, which is what allows the window's minimum to come
  down. A render transform looks identical in a screenshot while the window goes on
  demanding its full height — the bug would appear fixed and not be.
- **Scale from the declared MINIMUM, not the design size.** A minimum is the author's
  statement of "below this the layout breaks"; content beyond it is already the
  ScrollViewer's job. Scaling from the design size shrinks far more than necessary.
- **`Visual.PointToScreen` returns DEVICE pixels; every WPF size is device-INDEPENDENT.**
  Comparing them raw cost a diagnosis: on a 150% display a correctly-placed button
  reported `y=1087` against a 752 work area and looked catastrophically off screen, when
  1087 device pixels is 725 DIPs and comfortably inside. Convert with
  `PresentationSource.FromVisual(x).CompositionTarget.TransformFromDevice` first. The
  dangerous direction is the other one: on a 100% monitor the raw comparison passes, so a
  genuinely broken layout would have looked fine.
- **`TransformToAncestor(window)` then `window.PointToScreen(...)` double-counts** any
  transform between the element and the window. Call `PointToScreen` on the ELEMENT — it
  walks the whole chain once.
- **A `Popup` DOES inherit an ancestor `LayoutTransform` for rendering** — measured, not
  assumed: a ComboBox drop-down under a 0.820 window draws its items at 0.821. Worth
  knowing because the opposite is true for *bindings* (see `CalendarSizing`), so the
  separate-visual-tree rule does not generalise from one to the other.
- **`Math.Clamp` throws when max < min.** Pulling a window into the work area hits this
  the moment the window is wider than the screen; the sane answer there is to align to
  the near edge (which carries the title bar) rather than to throw.
- **The SCREEN is an input, not ambient state (2026-07-29).** A fitting harness that read
  `SystemParameters.WorkArea` passed on the 1280×752 laptop it was written on and failed
  on a 2057×1323 desktop the same week — not because fitting broke, but because nothing
  needed fitting and every assertion had gone vacuous. `WindowFitting.Fit` therefore takes
  a `(Window, Rect)` overload and the monitor-reading one is the convenience wrapper. Any
  rule whose input is "the machine you happen to be on" needs that seam or it can only be
  tested on one machine.
- **Force `UpdateLayout()` before measuring a freshly shown window.** A dispatcher pump is
  not enough: `Fit` derives chrome from `window.ActualHeight - root.ActualHeight`, and an
  unarranged root reads 0, so the call returns 1.0 and does nothing — silently failing
  every geometry assertion after it. The app never hits this because its class handler
  runs on `Loaded`, which is after the first arrange.
- **A harness assertion that dereferences the thing it is testing crashes instead of
  failing.** `transform!.ScaleX` after "assert transform is not null" reports a CRASH where
  the honest answer is one failed check and a clean run of the rest. Guard and return.

## A shipped list that changes SHAPE strands every code stored against the old one (2026-07-30)

Canada shipped as three provincial jurisdictions (`CA-ON`/`CA-AB`/`CA-BC`) and became one country
entry (`CA`). Every shop in the field still holds a provincial code, and `TaxJurisdictions.Find`
answers null for all three — correctly, because they are no longer shipped.

**The trap is that the fallback appeared to work.** `For(shop)` fell through to the home market, the
home market IS Canada, so every assertion and every screen showed the right answer — by coincidence.
The day the default moves, every one of those shops silently changes tax treatment, and nothing in the
codebase records that the two were ever different questions.

- **Widen at the RESOLUTION point, not in the lookup.** `For` now reads a code as
  `<country>-<region>` and falls back to the country entry (`CA-ON` → `CA`, `US-CA` → `US`) before the
  home market. `Find` stays strict: the settings screen relies on its null to tell a live code from a
  dead one, so folding the widening into it would hide a retired code instead of surfacing it.
- **Do not migrate the stored codes.** Rewriting `CA-ON` → `CA` in the database destroys the only
  record that the shop is in Ontario, for no gain — the resolution already answers correctly, and a
  re-added province takes effect on its own the day it ships. A migration here is a one-way door.
- **Keep the dead label keys**, marked dormant. Re-adding a province is then a line of JSON with its
  name already translated in five files, which is what makes "the presets are data" true rather than
  aspirational.
- Assert the retired codes explicitly. `taxcheck` now drives a shop stored as `CA-ON` through the
  settings screen and asserts the picker opens on Canada — the upgrade path, not a hypothetical.

## A second pricing mode is a second VOCABULARY, and some rows have no translation (2026-07-30)

The money was already right in both modes; what was wrong was every word around it. A tax-inclusive
order was still labelled `Order.Fields.PreTaxServiceTotal` over a price that is not pre-tax, and its
rate box still switched between a deposit rate and a final rate that cannot differ where the tax is a
property of the sale.

- **Some rows must be DELETED, not reworded.** The deposit-stage breakdown showed subtotal, balance,
  stage tax and post-tax total — where the tax is inside the price those are the price, the price
  minus the deposit, zero, and the price again. Four rows of arithmetic that always cancels is not a
  breakdown, it is a puzzle. It is now collapsed outright in that mode.
- **The rows that survive want a different ORDER**, which is why the inclusive final stage is a
  SIBLING panel rather than the same grid with rows hidden. `Grid.Row` is fixed in markup, so reusing
  it would have meant renumbering rows from code — the exact mechanism by which two views of one
  calculation drift apart.
- **Write both panels in ONE pass, from one reading of the split.** `UpdateTaxBreakdownLines` calls
  `UpdateDueAndReceivedLines` and `UpdateInclusiveBreakdown` back to back off a single
  `SectionPayment`. A panel that fetches its own figures is a panel that will one day disagree.
- **The tax has a NAME, and it is not the tax number's name.** A jurisdiction declares `taxNameLabel`
  (→ `TaxName.*`) separately from `taxNumberLabel`: Japan issues a qualified-invoice NUMBER for a
  consumption TAX, so deriving either from the other prints the wrong word. Only the inclusive
  entries declare one, because it is only read where the price contains the tax — declaring it for
  Canada and the US would be data nothing reads.
- **The receipt takes the same words as the screen**, through one `static TaxLabelConverter.Label`.
  The receipt is the copy the customer keeps; when the two disagree it is the paper that gets waved.

## A harness that types a price into a section can still be measuring zero (2026-07-30)

`taxcheck`'s new panel section set `AlterationPriceBox` to 1000 and every figure read 0.00. The
alterations category defaults to "None", which switches the service OFF, and a switched-off section
contributes nothing whatever the price box holds. Same family as "harnesses must seed their own
fixture": pick the charged category first, then type the price. The failure is quiet — the panel
renders perfectly, with zeros.

## An implicit style in a window REPLACES the theme's; a keyed one never inherits it (2026-07-30)

Two halves of one WPF rule, and both were live in this codebase at once.

- **`<Style TargetType="TextBox">` in a Window's resources does not extend the theme's implicit style —
  it replaces it.** `CustomMadeServiceWindow` declared one to add a read-only trigger, and thereby
  dropped every input in that window to the stock control: different height, padding, border and focus
  behaviour from every other screen, for years, while compiling and behaving perfectly. `BasedOn` is
  the whole fix.
- **A KEYED style with a TargetType does not pick up the implicit one either.** So when the radio
  template was themed, `MethodRadio` (42 radios in the order editor) and `ShopSetupWindow`'s
  `ModeRadioStyle` would have silently kept the stock look while every unkeyed radio changed — the
  worst outcome, since a half-restyled application reads as a rendering bug rather than a missed edit.

**Before restyling a control type, grep for every `<Style … TargetType="ThatType">` in the tree** and
sort them: needs `BasedOn`, or is deliberately bespoke (`FilterChip` and `ChallengeBox` carry their own
full templates and should be left alone).

## A harness that FLAPS is reporting another harness, not the code (2026-07-30)

`langcheck`'s "at least one shop installs every shipped language" went red, then green for several
runs, then red again, while nothing about language resolution changed in between. Chasing it by the
assertion text leads nowhere; the tell was in its own dump — **Montreal Atelier was `#4` in one run and
`#14` in a later one**. A shop had been deleted and re-created in the LIVE database.

The culprit is `storecheck`, which exercises delete and restore. Both harnesses copy the live file, but
one of them changes what the live file CONTAINS between suite runs, and `langcheck` asserted on whatever
happened to be there. Ordering makes it worse: `langcheck` runs before `storecheck` alphabetically, so
it sees the PREVIOUS run's leftovers.

Fixed by the rule already in this file — a harness must ESTABLISH the state it asserts on.
`SeedEveryLanguageShop` gives the fixture COPY a shop installing every language when the live data has
none. What is under test is `ShopLanguages.Installed` resolving a full set, not whether this machine
happens to own such a shop; the old version reported the difference between those two as a regression
in the first.

**And the wider point: a flapping gate silently devalues every result it has ever produced.** This suite
is what each change in this session was verified against. Two green runs either side of a red one are
not evidence the red was noise.

## Filling a field on the user's behalf STATES something (2026-07-30)

The split's auto-fill was first built to "settle the other rows at zero" when one was clicked into —
taken from a request that said "the rest becomes 0". It balanced the stage in one click and it was
wrong, in a way that only shows up a step later: a typed 0 is an ANSWER ("nothing was taken this way"),
so writing it on the shop's behalf both asserted something nobody had said and stopped those rows from
ever offering the remainder again. The next edit could not walk down the list.

The rule that survives: **the only row an auto-fill may write is the one the user is in.** Everything
else is either an answer already given or a question still open, and a placeholder — which charges
nothing — is how an open question offers help.

Same reason `SplitRow` keeps blank and 0 as different states rather than parsing both to zero.

## A control's enabled state has ONE owner, and the last writer wins (2026-07-30)

The v4.0.1 gate — "deposit received" cannot be ticked until a split deposit's rows add up — was first
written into the refresh pass, next to the figures it depends on. It had no effect whatsoever.
`ApplySectionLock` runs afterwards and assigns `c.DownCompletedCheck.IsEnabled = !sectionLocked`
unconditionally, so the gate was overwritten a few milliseconds after being applied.

That method already carries a comment about the same class of bug from the other direction (it used to
only ever set false, stranding controls). The rule both halves point at: **a control's enabled state
belongs to exactly one method**, and a new condition goes INTO that method rather than beside the data
that motivated it. The gate is now a predicate — `IsSplitDepositBalanced` — that `ApplySectionLock`
consults.

Worth noting how it was caught: four assertions in `splitcheck` failed with everything else green, all
saying the same thing. A UI rule asserted only through the model would have "passed".

## `IsChecked="True"` in markup fires its handler DURING InitializeComponent (2026-07-30)

The v4.0 split toggle shipped as `<RadioButton IsChecked="True" Checked="OnSplitModeChanged"/>`. Every
order window then crashed on OPEN with a `NullReferenceException` deep inside `RefreshComputedTotals`:
WPF raises `Checked` while the XAML is still being parsed, so the handler ran before
`InitializePaymentSectionControls` had assigned a single field.

The build was clean and the model harness was green — only a harness that OPENS the window caught it.
Two fixes, and both are worth having:

- **Put the default in code, after the controls exist**, not in markup. The other radios on this form
  never had one, which is why the trap had not been sprung before.
- **Guard the handler with a "controls are built" flag** (`_sectionsReady`), because the next person to
  add a markup default should get a no-op rather than a crash. `_syncingPayment` does NOT cover this:
  it is false at parse time.

## Tax follows the TENDER, so a per-portion rate cannot express a split (2026-07-30)

v4.0 lets one stage be paid several ways. The old model had one method and one rate per portion, so a
600 deposit paid 400 cash + 200 card could only be recorded as *one* of those — and taxing the portion
at the card's rate gives 78.00 where the right answer is 26.00.

`Order.PortionTax` now takes the portion's base AND an optional line list: no lines means the rule the
application always had, lines mean each is taxed at its own method's rate. Two things worth keeping:

- **The unsplit path is deliberately not "a split with one line".** It is reachable with no method
  chosen at all, and it charges on the portion's own base rather than on what the lines add up to — so
  a half-typed allocation still shows the tax on what is actually owed. `splitcheck` pins down that the
  two agree exactly where one line covers the portion, which is the property that matters.
- **`PaymentTaxRules.Active` is consulted per line, not per stage.** A shop that makes credit cards tax
  free changes a split the same way it changes an unsplit order, and the stored per-line rate is what
  keeps a reprinted receipt honest.

## A harness whose assertion COUNT moves is telling you something (2026-07-30)

`menucheck` reported 35 passed in one suite run and 33 in the next, both with zero failures. Nothing
had regressed: several of its assertions sit behind guards like `if (headerXs.Count == names.Length)`
or `if (rule is not null)`, which depend on a context menu having been measured. Under suite load a
measurement comes back 0 and the whole block is skipped — silently, and reported as a clean run.

Standalone it is 35/35, so the guards are what vary. **A conditional assertion is a test that can
delete itself under load.** Where a guard is genuinely needed, count the skips and print them, so a
run that checked less than the last one says so instead of looking identical.

## Probe for the FILE, not for the folder it lives in (2026-07-30)

`SystemSettingsPaths.SystemDirectory` returns the first root that has a `Settings/System` folder —
beside the exe, else the working directory. Every shipped-defaults path was built on top of that, so a
root holding that folder with only SOME of its files won the probe and the rest read as absent. Each
loader answers "absent" by degrading to its built-in fallback, silently.

It surfaced as a wrong phone number: a harness output directory held a partial copy (`app-defaults.json`
and nothing else), so `PhoneCountries` fell back to its single home-market entry, a stored `"+86 …"`
matched no dial code, and the field rendered `+1 +86 20 1234 5678`. Nothing threw. Two harnesses in the
same suite disagreed for no visible reason — the one with NO `Settings` folder in its output fell
through to the working directory and was fine.

`DefaultsFile(name)` now probes each root for the file itself and only falls back to the base-directory
path so an error message still names something a person can look at. **The general rule: when a probe
picks between candidate locations, ask it about the thing you actually need.** A directory-level answer
is a guess that the directory is complete.

## A relative ResourceDictionary URI resolves against the APPLICATION, not the assembly (2026-07-30)

`AppTheme.xaml` gained `<ResourceDictionary Source="/Themes/Flags.xaml"/>`. The app ran fine. Every
harness died on startup with `IOException: Cannot locate resource 'themes/flags.xaml'` — before a
window opened, so it read like the harness was broken rather than the theme.

A relative pack URI is resolved against the application currently loading the dictionary. Inside
`CameywareOrder.exe` that is the same assembly, so it works; inside `validcheck.exe`, which merges
`pack://application:,,,/CameywareOrder;component/Themes/AppTheme.xaml` by hand, it goes looking in
**validcheck** for a Themes folder it does not have. Any dictionary that a *different* executable can
merge must name its assembly:

```xml
<ResourceDictionary Source="pack://application:,,,/CameywareOrder;component/Themes/Flags.xaml"/>
```

The general rule: nesting a dictionary inside `AppTheme.xaml` is the right way to reach the harnesses
(they merge that one file), but the nested Source must be absolute or it reaches only the app.

## Windows has no flag emoji, and a picker that needs one must draw it (2026-07-30)

`🇨🇦` is two regional-indicator letters, and Segoe UI Emoji renders them as the letters — Microsoft
leaves the flag glyphs out deliberately. There is no font to install around it. The dial-code picker
draws six flags as `DrawingImage` in `Themes/Flags.xaml` instead: vector, no binaries in the repo,
crisp at 200%, and keyed `Flag.<code>` so the row resolves its own by country code. Simplified on
purpose at 20×14 — the US canton carries eight suggested stars, not fifty. `phonecheck` asserts every
shipped country HAS one, because the failure mode is a blank space in a list, which reads as a
rendering glitch rather than as missing data.

## A control that replaces a TextBox breaks the abstractions built on TextBox (2026-07-30)

Swapping the order form's phone `TextBox` for a `PhoneNumberField` broke `RequiredTextField`, which
held a `TextBox` and asked it for `.Text`. The fix that preserved behaviour was to make the record hold
two closures (`Func<bool> IsBlank`, `Action Focus`) with a `For(TextBox…)` factory for the ordinary
fields — NOT to lift the phone out of the one-pass required check, which is what would have quietly
undone "two missing fields are reported as two".

The same swap breaks harnesses that reach a control by name and cast it (`(TextBox)Field(window,
"PhoneNumberBox")`). Those are compile-time failures in the harness, which is the good case — but only
if the harness is actually run.

## A horizontal StackPanel makes `TextWrapping` inert (2026-07-30)

Two "the label overflows" reports had the same cause and neither was the label. A horizontal
`StackPanel` measures its children with **infinite** available width, so a `TextBlock` inside one is
never told it is too wide and never wraps, whatever `TextWrapping="Wrap"` says. The text simply runs
past the panel and is clipped by whatever is downstream. Setting the property looks like a fix and
changes nothing, which is the expensive part — it invites a hunt for the wrong bug.

The basic-info labels were `StackPanel Orientation="Horizontal"` holding an icon and a caption; they
became `DockPanel` with the icon `DockPanel.Dock="Left"`, which gives the caption the remaining width
as a real constraint. `VerticalAlignment="Center"` on the panel then does what was asked of it — a
two-line label sits centred against its field instead of riding the top.

The rule: **if a `TextBlock` will not wrap, look at what measures it, not at the `TextBlock`.**
Infinite-width parents are `StackPanel` (in its orientation), `ScrollViewer` (in its scrollable
direction), `Canvas`, and any `Grid` column sized `Auto`. The breakdown labels were the `Auto`-column
case in disguise — fixed-width `120` columns, so wrapping was live but the column was too narrow to
hold "Pre-Tax Service Total" on one line; those went to `158`.

Verify by RENDERING. Both of these compiled, ran, and asserted green the whole time they were wrong.

## The window's own copy of a theme style is where the theme stops (2026-07-30)

`OrderEditWindow` declared `<Style x:Key="FieldLabel" TargetType="TextBlock">` in `Window.Resources`
with no `BasedOn`. Same key as the theme's, so every label in the window resolved to the LOCAL one and
silently lost every setter the theme provides. Adding `TextWrapping` to the theme's `FieldLabel` changed
nothing in the window that needed it, and the long label went on clipping. This is the third time this
exact shape has cost a debugging session — `CustomMadeServiceWindow`'s `TextBox`, `MethodRadio`, now
this — so it is worth stating as a rule:

**A keyed style with a `TargetType` and no `BasedOn` REPLACES; it never extends.** Before editing a
theme style, grep for its key across `Views/` — if a window declares its own, the edit does not reach
that window. The fix is `BasedOn="{StaticResource SameKey}"`, which is legal and resolves to the
parent (merged) dictionary's entry rather than to itself.

## Format-as-you-type: read the CHANGE, not the selection (2026-07-30)

Re-grouping a phone number on every keystroke needs the caret put back afterwards, and the obvious
source — `TextBox.SelectionStart` inside `TextChanged` — is not reliable. Whether the box has already
moved the caret past the new text depends on how the text arrived: a keystroke, a paste, `SelectedText`
and an assignment to `Text` do not agree, and a harness cannot fake real keyboard input (see above), so
the difference shows up in production and not in the suite. `TextChangedEventArgs.Changes` gives
`Offset + AddedLength`, which is exact for all four routes.

Two more things that make such a field usable rather than merely correct:

- **Emit a separator only when a digit follows it.** A trailing dash appearing at three digits puts the
  caret behind punctuation the user did not type, and the next keystroke has to step over it.
- **Backspace onto a separator must take the digit in front of it.** Deleting the separator alone is
  undone by the re-group that immediately follows, so the key reads as doing nothing at all.

And restore the caret by DIGIT index, never character index: the re-group inserts and removes
separators on both sides of it, so "four digits in" is the only landmark that survives.

## A rule that fits the finished value can still be wrong on the way there (2026-07-30)

`phonecheck` asserted `FormatNational("0312345678") == "0312345678"` — Japan has no ten-digit grouping,
so the number comes back untouched — and it passed. The RENDER showed `031-2345-678`. Both were right:
the harness passed clean digits, while a person types them one at a time and the value passes through
nine digits, which the eleven-digit mobile pattern *does* group. The punctuation collected on the way
was still there at ten.

Progressive input has intermediate states that no assertion about the final value can reach. Either
drive the harness the way a person drives the control — one keystroke at a time — or render it. This
one was caught by rendering, and the fix was to return bare digits for a length the country accepts but
has no pattern for, so borrowed punctuation is taken back off rather than frozen in.

## Run the harnesses from the PROJECT ROOT, or the shipped presets vanish (2026-07-30)

`SystemSettingsPaths` probes `AppContext.BaseDirectory` then `Environment.CurrentDirectory` for
`Settings/System/Defaults`. A harness's own bin has neither, so run from anywhere but the project root
and every shipped preset reads as absent — and each loader answers a missing file by degrading
**silently** to its built-in fallback: one phone country instead of six, one tax jurisdiction instead of
six. Nothing throws.

What that looks like is assertions about unrelated things going red. `taxcheck` died on
`TaxJurisdictions.Find("US")!` returning null, 300 assertions in; `shopcheck` reported a stored
`+86 20 1234 5678` coming back as `+1 +86 20 1234 5678`, because with only Canada loaded no dial code
matched and the home market's `+1` was pasted in front. Neither had anything to do with the change
being tested, and both reproduced on a clean checkout — which is the check worth running FIRST when a
harness fails somewhere your diff never touched.

Some harness bins carried a stale copy of those files from an older csproj and passed by luck for
months; an incremental build swept the copy away and six assertions went red at once, in two harnesses,
in the middle of an unrelated change. `run-suite.ps1` now sets the working directory itself, and says
why.

## A display format that an EDIT BOX is seeded from is not a display format (2026-07-30)

Tax rates were `decimal` end to end and persisted perfectly. Quebec's 14.975% still could not be kept,
because every display used `"0.##"` — and the settings screen seeds its rate box from that formatted
string. Opening the tax settings for something else and pressing Save rewrote 14.975 as 14.98. Nothing
validated wrongly, nothing threw, and the stored value was correct right up until a person looked at it.

**Wherever a formatted value is written back into an editable control, the format is part of the data
path and must be lossless.** The tell is `box.Text = value.ToString(...)` — every rounding decision in
that format is now a rounding decision about what gets saved.

Two things follow from fixing it in one place (`Models/TaxRateFormat.cs`) rather than nine:

- The input filter, the parser and the display cannot disagree about the limit. They had already
  drifted: the box accepted any text at all while the parser demanded 0..100 and the display quietly
  rounded, so three different answers to "what is a rate" were live at once.
- A partial-input pattern must accept what a half-typed value looks like — `""`, `"14"`, `"14."`,
  `"14.9"` — and must NOT apply the range, or `"1"` is refused as the first keystroke of `"14.975"`.
  Range belongs to the finished value only.

A regression test for this has to drive the SCREEN and save twice. Asserting the stored decimal alone
passes throughout, because storage was never what broke.

## Display formatting hid the absence of money rounding entirely (2026-07-30)

There was no rounding anywhere in the money path — one `Math.Round` in the whole codebase, and it was
for measurements. It went unnoticed for months because `decimal.ToString("N2")` rounds half away from
zero, so every figure on screen was correct while the values behind them carried full precision. The
defect only shows where two of them meet: a total that is the sum of unrounded parts, printed beside
parts that were each rounded for display, does not add up.

**Round derived amounts at the calculation, not at the label.** A number that is displayed rounded and
stored unrounded is two different numbers with one name.

**Round the PARTS, then add them** — the opposite of what feels precise. Three lines of 0.10 at 5% are
0.005 each; rounding the total gives 0.02 under three printed 0.01s. Every figure a customer can see is
a figure they can add up, so each one has to be real. Costs at most a cent against the unrounded ideal.

Half goes UP (`MidpointRounding.AwayFromZero`), not to even. `decimal.Round` defaults to banker's
rounding, which turns 89.425 into 89.42 — a till arguing with a figure the customer worked out on paper.

A third decimal on the tax RATE is what made this everyday rather than rare: 14.975% lands on a
half-cent constantly where a two-decimal rate almost never did. A precision change upstream turns a
latent rounding question into a daily one.

## Run the analyzer; do not trust the Problems view (2026-07-30)

The Sonar gate was "check the IDE before building", and the first time it was run as an analyzer
package instead — `SonarAnalyzer.CSharp` in `Directory.Build.props`, so the rules run inside
`dotnet build` — it reported **9 issues across 6 files**, several months old, in a workspace that had
been called Sonar-clean repeatedly. A gate you have to remember to walk through is not a gate.

It is now permanent, so the baseline is zero and anything reported is new. Worth knowing about the
findings themselves:

- **S6605 / S6602** (`Any`→`Exists`, `FirstOrDefault`→`Find` on `List`/array) fire only on the
  concrete collection types, so they appear as code moves from `IEnumerable` to `List`. Mechanical.
- **S125** on a PROSE comment is usually a trailing semicolon making the line parse as a statement.
  Reword the sentence; do not suppress.
- **S4144** (two methods with identical bodies) was real: two split-mode handlers had the same body and
  neither read `sender` or `e`. The distinction they were named for lives in the DATA
  (`DepositEnabled`/`FinalEnabled`), so one handler serves both. Check what actually distinguishes the
  two before merging — if it is only the name, the name was documentation, and a comment says it better.

## "Did anything change?" — ask EF, and beware what the FORM writes on open (2026-07-30)

Change detection on the order editor compares the tracked entity against what EF loaded
(`db.Entry(order).Properties.Any(p => p.IsModified)`) rather than hashing the form. EF holds the
loaded values and compares column by column, so it covers every mapped field — including JSON blobs
the form does not model — and keeps covering a column added next year without anyone extending a list.

Two things it needs to work:

- **Do not write the audit stamp inside the apply-the-form method.** An unconditional
  `LastModifiedDate = DateTime.UtcNow` makes every save look like a change, which is exactly the
  question being asked. Stamping moved to its own method, called only when the check says yes.
- **Stop removing and re-adding child rows.** The items were `RemoveRange`d and re-added on every
  save, so the entity graph always reported changes. They are now compared by value first.

The finding worth remembering is the third one: **a record can be genuinely changed by merely opening
it.** An order stored before some field existed comes back with nulls the form cannot represent, and
the editor supplies its defaults — so the first save writes `Downpayment`, `DownpaymentMethod`,
`FinalBalanceMethod` and correctly stamps. That looks like broken change detection and is not. The
harness names the columns a no-op save moved, which is what turned "detection is broken" into "this
record was not in a state the form can round-trip"; without that diagnostic the temptation is to go
and suppress the stamp.

## The infinite-width StackPanel caught me AGAIN, one release after writing it down (2026-07-30)

`SessionActionWindow`'s choice cards were `StackPanel Orientation="Horizontal"` holding a glyph and a
two-line description. The description had `TextWrapping="Wrap"` and was clipped mid-sentence — the
identical defect this file already documents from v4.0.2, made while the rule was three screens up.

Knowing the rule did not prevent it; **rendering** did. The lesson is not "remember harder", it is
that any horizontal composition of an icon and prose gets a `DockPanel` by default, and that a new
window is rendered before it is called done. An assertion cannot see a clipped sentence.

## A fallback hides the bug AND the test for it (2026-07-30)

The lock screen showed `UserAccount.DisplayLabel` under a field labelled `Login.UserName`, above a
password box — naming the person while authenticating the login. `DisplayLabel` is
`PersonName.Label(FirstName, LastName, UserName)`: it reads as the person's name when the account has
one and **falls back to the user name when it does not**.

That fallback is why it shipped, and it is why the harness could not have caught it: the test account
was created with no first or last name, so both readings produced the same string and the assertion
passed against either. A test fixture in the fallback case cannot distinguish the two branches it is
supposed to be choosing between.

**When a value has a fallback, the fixture must sit on the side that exercises the real path.** The
regression test now builds an account WITH a name, and was confirmed to fail against the old code
before the fix went in — which is the only way to know an assertion is load-bearing.

Related: `DisplayLabel` is right in PROSE ("Signed in as Mei Lin · Toronto Atelier") and wrong in a
field labelled with a credential. Same value, opposite answers, decided by what the label promises.

## Leniency belongs to the VALUE, not to the record (2026-07-31)

Phone validation applied the strict per-country rule only to NEW orders: `_existing is null ?
IsValid : IsValidLoose`. The reasoning was sound — an order taken last year must stay saveable
without re-typing a number nobody can verify — but it was attached to the wrong thing. Keyed to the
ORDER, it meant an existing one accepted any 7-to-15-digit number in any country, including one typed
just now with the customer standing there.

**Leniency for legacy data should be keyed to whether the VALUE was touched, not to whether the
record is old.** `PhoneNumberField` remembers what `Load` put on screen (after any re-formatting, so
the control's own tidying does not read as an edit) and reports `HasBeenEdited`; untouched keeps the
old rule, anything retyped gets the current one. Both properties, instead of trading one away. Worth
reaching for wherever a "grandfathered" rule exists.

## Validate a phone number by PATTERN, not by digit count (2026-07-31)

Counting digits per country accepted `0899903357` and `1899903357` for Canada (NANP area codes cannot
begin 0 or 1), `23800138000` for China (mobiles start with 1), `012345678` for France (the national
part drops the trunk zero) — nine such numbers in the first probe, every one the right length and
none of them real.

`nationalPattern` in `phone-countries.json` now decides, with the digit count kept as the fallback.
Three things that made it safe:

- **Match against the DIGITS only**, stripping punctuation first. Otherwise every pattern has to
  re-state which separators people type, six times over.
- **Anchor both ends.** An unanchored pattern matches a substring, so a long wrong number containing a
  right one passes — a validating regex that validates nothing. Asserted per country.
- **Assert a real number per country too.** A pattern refusing everything satisfies every negative
  assertion while being useless.

**And do not ship a pattern you cannot justify.** Japan writes `090-1234-5678` (11 digits, domestic
trunk zero), `90-1234-5678` (10, international, no zero) and `03-1234-5678` (10, Tokyo, with zero).
The first pattern written for it demanded a leading zero and broke the second — caught by an existing
assertion. Length is the only rule true of all three, so Japan ships no pattern at all, which is the
same call already made for its missing 10-digit FORMAT and for the same reason: the digits do not say
which convention is in use. A fallback that says "no rule" is better than a rule that is wrong.

## A shared CONTROL does not give you a shared RULE (2026-07-31)

`PhoneNumberField` exists precisely so the phone rule lives in one place — its own remarks say a
second copy of the rule is free to drift. It was hosted by both the order form and the custom-made
record editor, and only the order form validated anything: the record editor checked that the box was
non-empty and wrote whatever was in it. Every rule tightened on orders could be walked around by
editing a record instead. The email was worse — never checked at all, on a window that collects one.

Sharing the control shared the *inputs and the formatting*. The DECISION — which rule applies, strict
or lenient — stayed in the window, so there was one implementation and one omission rather than two
implementations. **Ask what each screen decides, not just what it displays.** The fix moved the
decision onto the control as `IsAcceptable`, where it needs no parameter: the control knows what it
loaded, so "untouched stored value" is something it can answer for itself.

That audit was then done, and three of the five hosts were wrong: `ShopSetupWindow` validated nothing
at all (the shop's own number, which prints on every receipt), and `StoreMembersWindow` and
`UserManagementWindow` both used the lenient rule unconditionally. Their comments explaining the
leniency were RIGHT about the reason and wrong about the subject — the same value/record confusion.
So: when a control is introduced to centralise a rule, grep its usages for who actually *calls* the
rule. Hosting it is not calling it.

**The assertion that stops this recurring is structural, not behavioural.** `phonecheck` reads the
SOURCE of every window hosting the field and requires it to call `IsAcceptable`, to validate an
email, and NOT to name `IsValid`/`IsValidLoose` directly — those two being the halves the shared rule
chooses between, so naming either is a window deciding for itself again. Driving the five that exist
proves today's behaviour; only the source check constrains the sixth window somebody adds next year.
Worth reaching for whenever the invariant is "every X must do Y" rather than "X does Y".

The harness asserts the two windows AGREE on the same inputs, rather than asserting each separately.
That is what catches a future divergence; two independent assertions both pass while drifting apart.

## A capability gate reaches further than the screen it is on (2026-08-01)

`MainViewModel.LoadOrdersAsync` refuses to load without `CanViewOrders`, which is right — a role that
may not see customers should not have their records in memory waiting to be bound somewhere else. But
it means **the view model now depends on a signed-in user**, and `pickupcheck` went red three
assertions deep the moment it landed: it built the view model directly and measured an empty list.

Two things fall out of that, both reusable:

- The harness could not simply sign in as `admin`/`admin`. The REAL `credentials.json` belongs to
  whoever installed the application, and its admin password had been changed — the sign-in failed with
  `InvalidCredentials` and the "fix" looked like a second bug. A harness that needs an identity must
  **install its own credentials fixture** (back up, write, restore in `finally`) exactly as `permcheck`
  does. Do not reach for the real one.
- Assert the gate from BOTH sides in the harness that tripped over it. `pickupcheck` now signs in,
  checks the list loads, signs out, checks it does not, and signs back in. The failing direction is the
  one the gate exists for and the one nothing else was covering.

## A code-set `Visibility` REPLACES a binding; it does not combine with it (2026-08-01)

Two of the print menu items bound `Visibility` to `SelectedOrder.HasCustomMadeService`. Adding "and
the user may print" on top of that is not possible by assigning from code — the assignment clears the
binding, so the two rules would take turns depending on which wrote last, and the bug would look
intermittent. Where a control's visibility has **two** conditions, drop the binding and compute both
in one place (`MainWindow.RefreshOrderActions`). Same rule as the enabled-state one already recorded:
a property has one owner.

## `IsMouseOver` on a `TreeViewItem` means "anywhere in my subtree" (2026-08-01)

A hover tint triggered on the item's own `IsMouseOver` lights up every ANCESTOR of the row under the
pointer — four highlighted rows for one cursor. Trigger on the row `Border` inside the template
instead (`<Trigger SourceName="Row" Property="IsMouseOver">`), which is bounded to the row itself
because the `ItemsPresenter` is its sibling rather than its child.

Also in that template: **a named `RenderTransform` inside a `ControlTemplate` is not a trigger
target.** `<RotateTransform x:Name="ArrowRotation"/>` + `<Setter TargetName="ArrowRotation" .../>`
fails the XAML compile with *"Cannot find the Trigger target … (The target must appear before any
Setters, Triggers, or Conditions that use it)"*, which sends you looking at declaration ORDER — the
real cause is that a Freezable is not a nameable template part. Replace the whole transform in the
setter instead.

## A stretched element with a `MaxWidth` is CENTRED in what is left (2026-08-01)

`HorizontalAlignment` defaults to `Stretch`. Give such an element a `MaxWidth` smaller than its slot
and WPF centres it in the remainder — so a capability's explanation, sitting under its name in a
`StackPanel`, drifted ~130 px right and read as a second column. Found by rendering. Any element with
`MaxWidth` inside a left-aligned block needs `HorizontalAlignment="Left"` said explicitly.

## An assertion harness renders the DLL it copied, not the one you just built (2026-08-01)

The harness projects `<Reference Include="CameywareOrder">` a `HintPath` into `bin/Debug`, and MSBuild
COPIES that assembly into the harness's own output. Rebuilding the application and re-running
`demoshot` therefore re-renders the PREVIOUS UI — twice in a row the screenshot came back byte-identical
and looked like the XAML change had had no effect. **Rebuild the harness after every application
build**, before trusting a render.

## A Calendar hands its cells the style you SET, never the one you declare (2026-08-01)

`ThemedCalendar` had carried this comment for releases: *"Set EXPLICITLY, not left to the implicit
style: Calendar hands each day button whatever `CalendarDayButtonStyle` holds."* The month and year
cells needed exactly the same treatment and had never been given it — a `<Style TargetType="CalendarButton">`
sat in the theme with a `MinWidth`, a font size and a cursor, and **not one of them had ever
applied**. Invisible for as long as the drill-up views were only passed through on the way to a day;
obvious the moment the settlement report started opening a calendar directly in Year mode.

Both properties, always, on any styled `Calendar`:

```xml
<Setter Property="CalendarDayButtonStyle" Value="{DynamicResource ThemedCalendarDayButton}"/>
<Setter Property="CalendarButtonStyle"    Value="{DynamicResource ThemedCalendarButton}"/>
```

Keep the implicit `BasedOn` alias as well, so a raw `Calendar` outside a DatePicker still inherits it.

**A Calendar in Year or Decade mode DRILLS DOWN; it never selects.** Clicking March moves to March's
day grid, so `SelectedDatesChanged` is silent and there is nothing to read. The transition itself is
the answer: handle `DisplayModeChanged`, take `DisplayDate` as the cell that was clicked, and guard on
`e.OldMode` — setting the mode when the popup opens raises the same event and would otherwise register
as a choice the instant the calendar appeared. Also note a Calendar always measures itself for SEVEN
rows of days, so the months view leaves a third of the panel empty unless the height is pinned.

## The one-owner rule, broken five hours after being relied on (2026-08-01)

The main window's month-summary strip had its capability check in `ApplyRolePermissions` and its
"the month has figures, so show it" line in `RefreshSummaryStrip`. Both wrote `Visibility`; the second
runs on every order reload, so it always won. A role that may not read reports saw the shop's takings
on the home screen the moment the list refreshed. **Both methods looked correct on their own** — which
is the whole failure mode, and why the fix is not "add the check in the second place" but "make the
second place the only place".

Worth asserting in the harness as a PAIR whenever a control's state has both a permission and a data
condition: the owner must check, and nothing else may write it. The second half is what keeps the trap
from being rebuilt; verify it by re-adding the removed writer and watching it go red.

## A title bar is not capturable, so assert it from the source (2026-08-01)

`RenderTargetBitmap` draws the client area only. A change to `ResizeMode`, `ShowInTaskbar` or the
window buttons produces an identical screenshot, so "render it before calling it done" has nothing to
offer here — the source assertion is the evidence.

And assert the PAIR, not the property: `ResizeMode="CanMinimize"` with `ShowInTaskbar="False"` yields
a window that can be minimised and then has no button anywhere to restore it. Adding the minimize box
to the login screen required changing both, because the login screen had deliberately been kept out of
the taskbar.

## Gotchas

- Edit the string tables under `Settings/System/Languages/<code>.lang.xml`; copies
  under `bin/`, `publish/` are build outputs. (The single root `Languages.xml`
  this once named no longer exists — it was split one-file-per-language.)
- The running exe locks itself — always kill before building. If launched under
  the VS Code debugger it can't be killed from the terminal (Access denied);
  ask the user to stop it.
- SonarLint flags are inconsistent; only some occurrences of a pattern are
  reported. Fix flagged items; re-analyze to confirm.
- For nullable `bool?` values that are *consumed*, prefer `.GetValueOrDefault()`
  over `is true` (SonarLint S1125 still flags `is true` there).
- **Never put `Click=` on menu items inside a Style `Setter.Value`** (e.g. a
  `ContextMenu` in a `DataGridRow` style): it won't wire to code-behind and fails
  with a mis-attributed `MC6007` ("Click is not an event on DataGridTextColumn").
  Attach the menu on `DataGrid.ContextMenu` instead; keep row styles to
  `EventSetter`s only. Right-click does not auto-select a DataGrid row — select
  it in a `PreviewMouseRightButtonDown` handler first.

## A stored flag that nothing reads is not a feature (2026-08-02)

`CredentialRecord.MustChangePassword` was written by `CreateRecord` on every account the product had
ever made, cleared by `SetPassword`, serialised into `credentials.json`, and **read by nothing at
all**. Its own XML-doc said so — "Reserved: seeded true for new accounts, not yet enforced anywhere" —
and that comment survived several reviews of the auth code, because a field that is set, stored and
documented reads as done. The hardening in v9.2.0 is mostly not new state; it is the enforcement the
state was always for.

Worth grepping for the shape generally: a persisted property whose only writers are constructors and
whose only reader is the serialiser. It is either dead weight or an unfinished guarantee, and the two
look identical from the declaration.

## Retiring a shipped credential: verify, never match on the name (2026-08-02)

The product used to seed `admin`, `manager`, `staff`, `test1` and `test2`, each with its user name as
its password. Removing them from `SeedAccounts` fixes new installations and does nothing for the ones
already out there, and the obvious follow-up — delete or flag every account with one of those five
names — is wrong twice over:

- **Deleting is not ours to do.** By now `staff` may be a real person with real order history. The
  shop's data is not deleted because we regret having created the account.
- **Flagging by NAME punishes the shops that did the right thing.** An installation that gave
  `manager` a real password a year ago would be told to change it again, which is a support call
  generated by a security fix.

So the upgrade **verifies**: for each historical (name, password) pair, hash the shipped password
against the stored salt and arm `MustChangePassword` only on a match. That costs one PBKDF2
derivation per matching name, which is why it lives behind the schema-version check in
`UpgradeAccountShape` rather than in `LoadOrSeed` where it would run on every launch forever.

The list of historical seeds must never shrink. An entry removed from it is an installation that
keeps a known credential indefinitely with nothing anywhere to report it — so `AuthCheck` asserts the
five names are still in it, and asserts the four retired names are *absent from* `SeedAccounts` using
literals rather than reading the same list, so one edit cannot remove both the account and the check.

## The rule that makes a forced password change mean anything (2026-08-02)

Forcing a change and then accepting the same value back moves the problem by exactly one dialog. The
load-bearing rule is not the length minimum — it is **the new password may not be the user name**,
case-insensitively and ignoring surrounding space, because that is the precise shape of every
credential this product used to ship. It is checked in `CheckPassword`, which `WritePassword` is the
only caller of, so the forced change, an administrator's reset, account creation and the roster's
"add someone" cannot disagree about it.

Corollary on the API: `SetPassword` takes `requireChange` as a **required** parameter, not a
defaulted one — the same reasoning as the tax-mode parameter in `SKILL.md` §4a. The two callers want
opposite answers (an administrator handing over a password wants the person to replace it; a harness
pinning a fixture password wants to sign in with it), and a default would let a future call site
inherit whichever one happened to be written in the service, silently.

## A harness that hashes a user file must touch the singleton BEFORE the baseline (2026-08-02)

`uicheck` hashes `credentials.json` either side of its run to prove it only reads it. Bumping the
credential file's schema version made that check go red on the first run after the change — because
`AuthenticationService.Instance` reads the file in its **type initializer** and upgrades it on that
first read, which happened *after* the baseline hash was taken. The write was entirely legitimate:
any launch of the application would have done the same thing.

The fix is one line and it generalises to every future schema bump: touch the singleton first, then
take the baseline. What the assertion is for is "this run does not write to the user's accounts", and
a one-time migration performed on first read is not this run writing.
