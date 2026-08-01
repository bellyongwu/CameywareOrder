# TODO / Checkpoints — CameywareOrder

Checkpoint log for work driven by the `wpf-dev` skill. **When a new
request arrives, append a new entry at the top of "Open / in progress" BEFORE
starting work.** Move finished entries to "Completed".

Entry format:

```md
### <YYYY-MM-DD HH:mm> — <short title>  [PENDING|IN PROGRESS|DONE]
- Ask: "<verbatim user request>"
- Plan:
  - [ ] step 1
  - [ ] step 2
- Notes: <files touched, build result, follow-ups>
```

## Open / in progress

### 2026-08-01 12:20 — v5.1.0: an expected pickup date the list is ordered and coloured by  [DONE]
- Ask: "添加一个取单日期，expected pickup日期必须要是future dates。\n\n这个日期是用户预定的要取货的日期。\n\n所有订单 如果取货日期距离当前日期还有2周，必须要把record的background color改为orange， 如果日期超过，就改为red。提醒用户订单是否快超时了。\n\n订单的排序不再按照last modified time去排序，而是按照取货日期。 而且取货日期是必填。不能留空 需要有validation， default是空，需要提醒用户。然后订单日期和预计取货日期必须放一排。"
- Plan:
  - [ ] `Order.ExpectedPickupDate` (nullable — every existing order has none) + a runtime column
        guard, `ExpectedPickupDateLocal`, and the due-state the colour reads
  - [ ] Editor: a second picker on the SAME row as the order date, blank by default, required, and
        refusing anything that is not in the future
  - [ ] List: ordered by pickup date instead of last-modified, a column showing it, and the row
        background orange within two weeks / red once past
  - [x] Keys × 5 languages; build; render; harness
  - [x] Mid-turn: "新建一个店铺，插入50条订单，我用来做demo" + "新建的demo数据要新建一个新的店铺。不要在现有的店铺操作"
  - [x] Mid-turn: "Upate <version> keywords for the repo, and mark the version 5.0.1 as a hotfix"
- Notes: **Model** `Order.ExpectedPickupDate` (nullable) + `ExpectedPickupDateLocal` + `PickupDue`
  → new `PickupDueKind` (None/Soon/Overdue, `PickupSoonDays` = 14; None for a FINISHED order too) +
  shared `ToStoredDate` (both dates now convert through it). Runtime column guard added.
  **Form** `PickupDatePicker` on the SAME grid row as the order date (order date moved to col 0/1,
  pickup to col 2/3); empty by default, blacked out up to and including today, `IsPickedUpDateAllowed`
  → `IsPickupDateAllowed` refuses a past day at save, and the blank case goes through
  `RequiredTextFields` so two missing fields still report as two. Read-only orders lock it.
  **List** default order is the pickup QUEUE (finished sink, then no-date, then by date asc);
  `ExpectedPickupDate` added to `GetSortSelector`; new column + detail-panel line; new
  `PickupDueBrushConverter` drives a `DueTint` layer in the row template.
  **Version** `Directory.Build.props` now carries Version/AssemblyVersion/FileVersion/
  InformationalVersion + Company/Product; shipped as 5.0.1-hotfix for the version keywords, then
  5.1.0 for this feature. README has an entry for each.
  **Demo** `scratchpad/demoseed` created shop #5 "Demo — Pickup Dates" with 50 orders (8 overdue,
  14 due-soon, 20 quiet, 8 finished). Verified it added exactly 1 shop and 50 orders and touched
  nothing else; the real database was backed up to the scratchpad first.
  Build 0/0. `scratchpad/pickupcheck` 67 assertions green; `datecheck` and `langscope` unaffected.
  *Found by rendering, twice:* the selection highlight painted over the overdue tint on the very row
  the ordering puts first (fixed by layering, now asserted), and finished orders sat above every
  overdue one because their pickup day is older (fixed by sinking them, now asserted).
  Gate 1/Gate 2 still unavailable — no IDE-diagnostics or SonarLint tool in this session.

### 2026-08-01 10:05 — v5.0.0: a reusable language scope, and the source tree split three ways  [DONE]
- Ask: "给量身项目设置添加一个切换可选语言的选择\n-目的，是为了检查改变前后的效果。\n注意：\n只改变当前量身项目设置的语言选择，不改变整体的语言设置。\n-目前，为了去看量身项目设置还要切换整体语言去查看，有点费劲。\n\n要求：把语言切换设定更加模块化，可以用来加载在任何panel上。\n\n做完后，commit和push，可以作为一个新的release start v5.0.0"
  Mid-turn: "Restructor application 把views folder分成三个folder 一个是users management 一个是store management 还有是Global"
  Mid-turn: "根据你的理解 把Models 和services也归纳一下新的folder。"
  Answered when asked: order/custom-made windows → StoreManagement; StoreMembersWindow → UserManagement.
- Plan:
  - [ ] `LocalizationScope` — a table reader bound to ONE language, independent of the global
        setting, declarable in any panel's `Resources` and bindable exactly like the singleton
  - [ ] `LanguageScopeSelector` — the reusable control that drives a scope; options from
        `ShopLanguages.Selectable`, so it obeys what the shop installs
  - [ ] `MeasurementTermsWindow` renders through the scope: its own labels AND the term/garment
        names, so switching previews the translation without touching the app's language
  - [x] `Localization/ILocalizedText` + `LocalizationScope`
  - [x] `Controls/LanguageScopeSelector`
  - [x] `MeasurementTermsWindow` renders through the scope
  - [x] Views / Models / Services each split into UserManagement / StoreManagement / Global
  - [x] Build; render; harness; README v5.0.0; commit + push
- Notes: **New** `Localization/ILocalizedText.cs` (indexer + Format; `LocalizationService` now
  implements it, so no call site changed), `Localization/LocalizationScope.cs` (follows the app until
  pinned; raises `"Item[]"` + `TextChanged`; `Detach()`), `Controls/LanguageScopeSelector.xaml(.cs)`
  (`Scope` DP, fills from `ShopLanguages.Selectable()`, collapses at one option, follows its scope
  BOTH ways). **Window** 22 binding sources repointed to `{StaticResource Scope}`; Title moved to
  code (`Resources` do not exist when a Window's own properties are set); `PreviewLanguage` replaces
  `CurrentLanguage` for names and panel copy while dialogs/warnings keep `_localization`;
  `TermRow` takes an `ILocalizedText` for its gender tooltip;
  `MeasurementGenderPresentation.NameText` now takes `ILocalizedText`. **Key**
  `Language.PreviewLabel` × 5 languages.
  **Move** `git mv` only — namespaces unchanged, nothing references a source path (checked: no pack
  URI, no MergedDictionary, no csproj item), `obj/Debug` deleted so the old generated partials did
  not compile alongside the new ones. Views: 5+1 / 11+1 / 3 files. Models: 1 / 13 / 2.
  Services: 1 / 15 / 4.
  Build 0/0. `scratchpad/langscope` 37 assertions green, `datecheck` 94 still green after the move,
  both renders looked at. *Found by rendering:* the picker did not follow a scope moved by its HOST —
  fixed, and now asserted in both directions. Gate 1/Gate 2 unavailable again (no IDE-diagnostics or
  SonarLint tool in this session); the in-build SonarAnalyzer pass is the whole Sonar evidence.
  Stale `Architecture.md` entries removed while there: `ManagementStyles.xaml` and
  `CurrencySettingWindow` no longer exist.

### 2026-07-31 21:40 — v4.3.0: the order date is a field, and it can be backdated  [DONE]
- Ask: "skill wpf-dev, create a field of date time picker for the order date field. right now it is
  using the date that we operate. but sometimes we need to record the order before the date because
  we may forgot to input the order realtime, we can record it later on. so for the record, do not
  that so speicific, just record the date should be good enough. if not edit the date field, just use
  the real time order date as default."
  Mid-turn: "So the order date cannot be later than the realtime date"
  Mid-turn: "Also, make datetime picker UI float right on align with the input field."
  Layout answer (asked): right-hand label/input pair, new row under Order Number / Status.
- Plan:
  - [ ] `OrderEditWindow.xaml`: a themed `DatePicker` in the Basic Info card, right column, new row
  - [ ] Seed it from the order's own date (new order = today); cap the calendar at today
  - [ ] Save writes a DATE, not an instant: local midnight → UTC, and only when the picked date
        differs from the one already recorded, so an untouched picker keeps the live timestamp and
        an untouched EDIT does not count as a modification
  - [ ] Refuse a future date on save (the calendar cannot offer one; the box can be typed into)
  - [ ] Read-only orders: the picker locks with the rest of the form
  - [ ] `Order.OrderDateLocal` — the list and the detail panel render the stored UTC instant RAW,
        so a shop in +08 would see a backdated order a day early. The receipt already converts.
  - [x] `OrderEditWindow.xaml`: a themed `DatePicker` in the Basic Info card, right column, new row
  - [x] Seed it from the order's own date (new order = today); refuse the future
  - [x] Save writes a DATE, not an instant: local midnight → UTC, and only when the picked date
        differs from the one already recorded
  - [x] Refuse a future date on save (the box can be typed into)
  - [x] Read-only orders: the picker locks with the rest of the form
  - [x] `Order.OrderDateLocal` for the list, the detail panel and the receipt
  - [x] Two new string-table keys × 5 languages; build; render; harness
  - [x] Mid-turn: the drop-down twice as WIDE (not taller), shared by every picker in the app
- Notes: **Model** `Order.OrderDate` documented + `OrderDateLocal` (`[NotMapped]`) +
  `static ResolveOrderDate(picked, recorded)` — unchanged DAY returns `recorded` to the tick, so an
  untouched save stays off EF's modified list; a changed day is `SpecifyKind(day, Local).ToUniversalTime()`.
  **Form** new row 1 in the Basic Info grid, right-hand label/input pair (rows 1-5 renumbered to
  2-6); `InitializeOrderDatePicker` (seed + `BlackoutDates` from the later of today and the stored
  day), `ApplyCalendarLanguage` called from `RefreshLocalizedLabels`, `IsOrderDateAllowed` in
  `ValidateForSave`, `OrderDateErrorText` in `ValidationErrorBlocks` + the clearing map, picker
  disabled in `ApplyReadOnlyMode`, one line in `ApplyEditableFields`. **Display** `MainWindow.xaml`
  list column + detail panel → `OrderDateLocal`, detail drops the time; receipt line → local day only.
  **Theme** `FontSizeCalendar` (15), day button `MinWidth 58` / `MinHeight 34`, blackout strike drawn
  in the custom template, `CalendarButton` sized to match; `CalendarSizing` sets `MinWidth` not
  `Width` so a narrow picker's drop-down grows instead of clipping. **Keys**
  `OrderEdit.OrderDateHint`, `OrderEdit.Validate.OrderDateFuture` × 5 languages.
  Build 0/0 (Sonar analyzers run in-build). `scratchpad/datecheck` — 94 assertions green, two renders
  looked at. Gate 1/Gate 2 could not be run as written: no IDE-diagnostics or SonarLint MCP tool is
  connected in this session, so the in-build SonarAnalyzer pass is the whole of the Sonar evidence.
  Three findings reported to the user rather than fixed, all pre-existing: an order whose service
  category is "None" clears its section on the next save; the legacy aggregate `FinalBalanceMethod`
  reconciles itself on the first re-save; and `CopyOneOrderAsync`'s property list is still behind the
  model (carried over from v4.2.0).

### 2026-07-31 19:22 — v4.2.0: select several records and act on them at once  [DONE]
- Ask: "use skill wpf-dev, create a feature for store to delete records in store. To do that, when
  activate on the main application, use CTRL+left click to select records. you can do batch options.
  just allow copy and delete records. also, CTRL+A hot key you can select all records in current page."
- Plan:
  - [ ] `OrdersListView` `SelectionMode` Single → Extended (ctrl-click toggles, ctrl+A selects the
        page; the same mode `StoreManagementWindow` already uses)
  - [ ] `MainViewModel` holds the whole selection, not just the anchor; Delete and Copy act on all of
        it; every other action requires exactly one row
  - [ ] Split the confirmation off the delete so a harness can drive the work without a modal
  - [ ] Batch copy must not issue duplicate order numbers — see the finding below
  - [ ] Selection-count badge beside the records count; new keys in all five language files
  - [ ] Harness + render; build 0/0; Sonar clean; README; commit
- Finding, in scope: `MainViewModel.CopyOrderAsync` composes `$"ORD-{DateTime.Now:yyyyMMdd-HHmmss}"`
  by hand rather than going through `OrderNumberFormatter`, so it ignores the shop's configured
  prefix and mode entirely. On a batch every copy made in the same second gets the SAME number.
  `OrderNumberFormatter.Reserve` cannot currently save it either: it returns early in Timestamp mode,
  before the collision scan its own summary promises.
- Finding, adjacent — REPORTED, not fixed: the copy's property list has fallen behind the model. It
  drops `PricesIncludeTax`, `PaymentSplitsJson`, `AlterationFinalTaxRate`, `ClothingFinalTaxRate`,
  `CustomMadeFinalTaxRate`, `StatusReason`, `StatusReasonCategory` and `LastModifiedBy`. The first
  five are money: a copy in a tax-inclusive market comes back priced under the other arithmetic.
- Notes: `MainWindow.xaml` (SelectionMode Extended + `SelectionChanged`, `HasSingleSelection` gates on
  Edit/the three Print items in BOTH menus, selection badge on `WarningSoftBrush`),
  `MainWindow.xaml.cs` (`OnOrdersSelectionChanged`, `OnSelectionRequested`, right-click replaces vs
  extends, Enter needs one row / Delete takes the batch, unsubscribe in `OnClosed`),
  `MainViewModel` (selection state + `SetSelection` + `SelectionRequested`;
  `ConfirmAndDeleteSelectedAsync`→`DeleteSelectedAsync`; `CopySelectedAsync`→`CopyOneOrderAsync`),
  `OrderNumberFormatter.ReserveTimestamp`, 5 new keys × 5 language files.
  Build 0/0 (Sonar runs in the build, so that is Sonar-clean too). CJK sweep: nothing introduced
  outside the language files. Key parity 659/659 across all five, one `{0}` per new key, no value
  equal to its English.
  **`scratchpad/batchcheck` — 49 assertions, green three runs running**, driving the real view model
  over a throwaway SQLite file and the real `MainWindow`; `credentials.json` hashed before/after.
  Ctrl+A is driven as REAL input (`keybd_event`) after winning the foreground, and fails rather than
  skips when it cannot — the first attempt asserted it through `ApplicationCommands.SelectAll` and
  found CanExecute false on a stock Extended ListBox too, i.e. it was testing a mechanism that does
  not exist. Falsification: reverting `ReserveTimestamp` turned "no two orders share an order
  number" red with three copies on `ORD-20260731-194353`, then restored byte-for-byte.
  Rendered both states and looked at them — the first pair was taken mid-fade (0.30s selection
  animation) and showed every row selected in the SINGLE-selection shot; `Settle(600)` fixed the
  harness, not the app.
  Follow-up left open: the eight fields Copy still drops (reported to the user, their call).

### 2026-07-31 02:40 — v4.1.3: every phone and email field, validated  [IN PROGRESS]
- Ask: "For all the phone number and email sections, you should apply the validations."
- Audit of all five hosts of `PhoneNumberField` before touching anything:
  - `OrderEditWindow` — fixed in v4.1.1.
  - `CustomMadeServiceWindow` — fixed in v4.1.2.
  - `ShopSetupWindow` — **no validation at all**, phone or email. The shop's own number goes onto
    every receipt, so this is the one that gets copied by the people who need it.
  - `StoreMembersWindow` — `IsValidLoose` unconditionally on the edit path, so a retyped number was
    never checked. The new-member path used `IsValid`, which was right but a second answer.
  - `UserManagementWindow` — `IsValidLoose` unconditionally, same as above.
- Notes: all six phone call sites now read `IsAcceptable` and all six email sites
  `ContactValidation.IsValidEmail`; `IsValidLoose` no longer appears in any window. The comments that
  justified the leniency were RIGHT about the reason and wrong about the subject — kept, and re-aimed
  at the stored value rather than the record.
  **The assertion that matters is structural**: `phonecheck` reads the SOURCE of every window hosting
  a phone field and requires it to call `IsAcceptable`, to validate an email, and NOT to name
  `IsValid`/`IsValidLoose` directly. Driving the five that exist proves today's behaviour; reading
  the source is what stops a sixth window skipping the rule, which is exactly how this whole run of
  fixes started. `phonecheck` 221 → 236.

### 2026-07-31 02:05 — v4.1.2: the custom-made record checks its contact details too  [DONE]
- Ask: "Create another fix to 4.1.2 apply the email validation and phone validation rules for Edit
  custom Record section as well."
- Findings: `CustomMadeServiceWindow` carries a `PhoneNumberField` and an `EmailBox` and validates
  NEITHER. `OnSaveClick` checks the phone is non-EMPTY and never checks it is a number; the email is
  read straight into the record with no check at all. So the rules just tightened for orders were
  reachable around by editing a custom-made record instead.
- Decision: the strict-or-loose DECISION moves onto `PhoneNumberField` as one property rather than
  being restated in a second window — a second copy of a validation rule is free to drift, which is
  the reason this control exists at all. The control already knows what it loaded, so it can decide
  alone: an untouched STORED number keeps the loose rule; a blank baseline or an edited value gets
  the strict one. That is exactly what `OrderEditWindow` computes today, so its behaviour is unchanged.
- Plan:
  - [ ] T1 `PhoneNumberField.IsAcceptable` — the one place the strict/loose choice lives
  - [ ] T2 `OrderEditWindow.ValidatePhoneField` uses it instead of its own copy
  - [ ] T3 `CustomMadeServiceWindow` validates phone and email on save, marking the phone red
  - [ ] T4 harness coverage: the custom-made window refuses what the order window refuses
  - [x] T5 build 0/0 + Sonar 0, suite, publish, docs, README v4.1.2
- Notes: `PhoneNumberField.IsAcceptable` needs no parameter — the control knows what `Load` gave it,
  so "an untouched stored value" is something it can answer alone, and both windows now ask the same
  property. `OrderEditWindow` keeps only the MESSAGE choice locally (the lenient path shows the
  generic wording), and its behaviour is unchanged: a new order calls `ResetTo`, which loads null, so
  its blank baseline resolves to the strict rule exactly as `_existing is null` did.
  `CustomMadeServiceWindow.OnSaveClick` gained the phone check (marking the field red and focusing
  it) and an email check; a small `Fail` helper stops the banner+dialog pairing being written out a
  fourth and fifth time. **The lesson is that sharing the CONTROL did not share the RULE** — one
  implementation and one omission, rather than two implementations — written into `context.md` along
  with the note to audit the other three hosts of this field the same way. `phonecheck` 209 → 221,
  and the new assertions check the two windows AGREE on the same inputs rather than checking each
  separately, because two independent assertions both pass while drifting apart.

### 2026-07-31 01:10 — v4.1.1: a retyped phone number is held to the rule  [DONE]
- Ask: "Fixing a bug on phone validation. 289-990-33577 I entered this number, but it still accepts.
  it shouldn't allow for the saving." + "For canada region" + "I haven't tried the other countries
  yet. do a deep dive on this section find out why"
- Deep dive (`phoneprobe`, a throwaway diagnostic that prints STRICT vs LOOSE for every shipped
  country across 6-13 digits):
  - The STRICT rule is correct everywhere. CA/US refuse 11 digits, FR/ES refuse 11, and CN/JP ACCEPT
    11 because 11 is a real mobile length in both — so the reported number is legitimately valid
    under China and Japan and invalid under the other four.
  - The hole is not in any country's rule. `ValidatePhoneField` chooses which rule to apply from
    whether the ORDER is new (`_existing is null ? IsValid : IsValidLoose`), so an EXISTING order is
    never held to its country's length at all. Every disagreeing row in the matrix — every country,
    every wrong length, 6 digits through 13 — is accepted on an existing order.
  - The save path itself is fine: `ValidateForSave` does call `ValidatePhoneField`.
- Decision: choose the rule from whether the NUMBER was edited, not from whether the order is new.
  The leniency exists for a number typed before the rule existed and that nobody can re-verify; it
  was never meant to cover a number typed just now with the customer standing there. Untouched keeps
  the loose rule, anything retyped gets the strict one — both properties instead of a trade.
- Plan:
  - [ ] T1 `PhoneNumberField` remembers what it loaded and reports `HasBeenEdited`
  - [ ] T2 `ValidatePhoneField` goes strict for a new order OR an edited number
  - [ ] T3 `phonecheck`: the existing assertion encodes the OLD behaviour and has to be re-aimed —
        untouched legacy number still saves, retyped number is refused, reported number refused for
        Canada and ACCEPTED for China
  - [x] T4 build 0/0 + Sonar 0, suite, publish, docs, README v4.1.1
- Mid-turn ask: "you can use regular expression for different countries phone number type for the
  validation. did you use this strategy?" — honest answer was NO: validation counted digits per
  country plus one shared shape regex. Measured what that misses before answering, and it was worse
  than expected: `0899903357`, `1899903357`, `2890990335`, `0125551234`, `23800138000`,
  `10800138000`, `012345678`, `112345678` were ALL accepted — every one the right length for its
  country and none of them a number that country issues.
- Notes: **T1/T2** `PhoneNumberField._loadedNumber` is captured in `Load` AFTER the regroup (a
  re-punctuation is the control's doing, not an edit) and `HasBeenEdited` compares `FullNumber`
  against it — country included, because the same digits are a valid Chinese mobile and an invalid
  Canadian one. `ValidatePhoneField` now goes strict on `_existing is null || HasBeenEdited`.
  **Regex** added as `nationalPattern` in `phone-countries.json`, matched against DIGITS ONLY so each
  pattern describes numbers rather than punctuation, with the digit count kept as the fallback for
  any country shipping none and an unparsable pattern degrading to that same fallback rather than
  taking the country down. NANP `^[2-9][0-9]{2}[2-9][0-9]{6}$` for CA/US, `^1[3-9][0-9]{9}$` for CN,
  `^[1-9][0-9]{8}$` FR, `^[6-9][0-9]{8}$` ES.
  **Japan ships no pattern, deliberately** — and my first attempt (`^0[0-9]{9,10}$`) was WRONG and
  caught by an existing assertion: this app takes `090-1234-5678` (11, domestic trunk zero),
  `90-1234-5678` (10, international, no zero) AND `03-1234-5678` (10, Tokyo, with zero). Demanding a
  leading zero refuses the second; forbidding it refuses the third. Length is the only rule true of
  all three — the same call as its missing 10-digit format, for the same reason.
  New key `OrderEdit.Validate.PhoneShape` in all five languages: quoting "a number here has 10
  digits" at somebody who typed exactly 10 reads as a bug in the check, so the length message is used
  only when the length is what is wrong. `phonecheck` 167 → 209, including a positive number per
  country so a pattern that refuses everything cannot pass.

### 2026-07-30 21:15 — v4.1.0: a save that changed nothing, and locking the session  [DONE]
- Ask: "Use skill wpf-dev: Have another version of release 4.1.0 A few Changes: 1. To detect if a
  record is modified or not, at least you changed something in the record. -add a content update
  checker, see if any changes against opened record. -if no change but still click the save, it is
  considered no change, then last update date time shouldn't update, and 经办人no need to update
  Feature: To make it more secure and more privacy. Add a new button to lock and unlock the account
  log in. so you don't need to signout and sign back in to select the store. Requirements: -On main
  application view, when you press ESC, you will see an themed UI panel to ask you if you want to Sign
  out or just lock. -If Sign out, then follow what we had now. If locked, then show the Locked sign in
  panel. have your account filled already. -when you add your password, you can open the store you just
  locked from. -you can add the lock button beside Sign Out button too. that Lock button pops up
  alert(same as Sign out) to show you the message that you are going to lock, and you can login back."
- Decisions taken before coding:
  - Change detection asks EF whether the tracked entity is modified, rather than hashing the form. EF
    compares against what is actually in the database, covers every mapped column including the JSON
    blobs, and cannot drift when a field is added later. The clothing items are the catch: the save
    path does `RemoveRange` + re-add, which always reads as a change, so they need a by-value
    comparison or the check answers "changed" every time.
  - A LOCK keeps the shop and the user; only the password is asked for again. Cancelling or closing
    the lock screen signs out to the login screen rather than returning to the session — a lock that
    can be dismissed is not a lock.
  - Locking runs the same open-editor guard as signing out. An order editor left open behind a locked
    screen would hold the record and defeat the point.
- Plan:
  - [x] T1 change detection: stamp `LastModifiedDate`/`LastModifiedBy` only when something changed
  - [x] T2 themed session panel (Sign out / Lock / Cancel), shown on ESC from the main window
  - [x] T3 lock screen: account named and fixed, password only, reopens the SAME shop
  - [x] T4 Lock button beside Sign Out, same confirmation as Sign Out
  - [x] T5 localization keys in all five language files
  - [x] T6 harness coverage for both
  - [x] T7 build 0/0 + Sonar 0, suite, render, publish, docs, README v4.1.0
- Notes: **T1** the stamp moved out of `ApplyEditableFields` into `StampLastModified`, called only
  when `db.Entry(order).Properties.Any(p => p.IsModified)` or the items differ. An unconditional
  `UtcNow` in the apply method was itself making every save look like a change. Items are compared by
  value (`ClothingItemsMatch`) instead of `RemoveRange` + re-add, or the graph always reported
  changes. **A record can be genuinely changed by OPENING it** — an order with null
  `Downpayment`/`DownpaymentMethod`/`FinalBalanceMethod` gets the form's defaults on load, so its
  first save writes them and correctly stamps; the harness names the moved columns, which is what
  turned "detection is broken" into "this row was not in a state the form can round-trip". Reported
  in the README as expected behaviour rather than papered over.
  **T2–T4** `SessionActionWindow` (Lock / Sign out / Stay here) and `LockScreenWindow` (account named
  and fixed, password only). `App.LockAsync` holds the account and shop id in LOCALS only — nothing
  about a locked session survives the process — calls the real `SignOut()` so no capability survives
  the lock, and reopens through `LoadSelectableShopsAsync` so access revoked while locked sends the
  user to sign-in. Neither window uses `DialogResult`: it throws on a non-modal window, which made
  them undrivable by a harness; both report through their own properties and `Close()`.
  **Sonar in the build earned itself immediately** — flagged `OnPasswordKeyDown` as static-able the
  moment it was written; it turned out to be dead (no `IsCancel` button means ESC cannot close the
  window anyway) and was deleted rather than suppressed.
  **Rendering caught a clipped sentence** in the choice cards: horizontal `StackPanel` again, one
  release after writing that rule into `context.md`. Knowing it did not prevent it; rendering did.
  New `lockcheck` harness, 23 assertions, registered in `run-suite.ps1`.
- Follow-up ask: "when screen locked, the user name should use user name field, it seems like you are
  using Firstname + Lastname. Please verify" — verified and real. `AccountText` was `DisplayLabel`,
  i.e. `PersonName.Label(FirstName, LastName, UserName)`, under a field labelled `Login.UserName` and
  above a password box: it named the person while authenticating the login. Now `account.UserName`.
  **The original assertion could not have caught it**: the temp account has no first or last name, so
  `DisplayLabel` falls back to the user name and both readings agree. Only an account WITH a name
  distinguishes them, so the regression test constructs one — and was confirmed to fail against the
  old code ('Mei Lin') before the fix went in. The session PANEL deliberately still greets by name:
  "Signed in as Mei Lin · Toronto Atelier" is prose about who is here, not a labelled credential.
  `lockcheck` 23 → 25.

### 2026-07-30 20:10 — v4.0.4: a tax rate with three decimals  [DONE]
- Ask: "Fix tax rate, make sure it can accept 3 numeric digts. for instances, Quebec can accept 14.975%
  as the sells tax rate. need to consider it"
- Findings before changing anything: the rate is `decimal` END TO END, so storage was never the
  problem — 14.975 persists fine. Every DISPLAY is `"0.##"` (9 sites), so it reads back as 14.98, and
  because the settings screen seeds its edit box from that same rounded string, re-saving WRITES the
  rounded value. The rate box also has no input filter at all, so a 4th decimal can be typed and is
  silently rounded on the next load.
- Plan:
  - [x] T1 one definition of the rate format (3 decimals) rather than a tenth copy of `"0.##"`
  - [x] T2 apply it at all 9 formatting sites
  - [x] T3 filter + validate the rate box, so 3 decimals is a rule rather than a coincidence
  - [x] T4 harness coverage: the round-trip that was losing the third decimal
  - [x] T5 build, suite, render, publish, docs, README v4.0.4
- Notes: New `Models/TaxRateFormat.cs` is the one definition — `MaxDecimals`, the partial-input regex,
  `TryParse` with the 0..100 range, `Text` and `Percent`. Nine `"0.##"` sites now call it
  (`TaxLabelConverter`, `TaxJurisdiction.DisplayName`, `ShopSetupWindow` ×3, `OrderEditWindow` ×2).
  `PaymentTaxRow.TryResolveRate` delegates rather than restating the rule — it decides what is SAVED,
  so it has to agree exactly with what the box allowed. The box gained `PreviewTextInput` +
  `DataObject.Pasting` (it previously accepted any text at all) and went 66px → 82px so "14.975" fits
  without scrolling. Three separate answers to "what is a rate" had been live at once: the box took
  anything, the parser demanded 0..100, the display rounded to two places.
  **The partial pattern must not apply the range** — `"1"` is the first keystroke of `"14.975"`.
  **Proved the test can fail**: stashing just `ShopSetupWindow` reproduced the defect exactly — box
  seeded `14.98`, and re-saving an untouched screen wrote `14.98` back. `taxcheck` 323 → 355.
- Mid-turn ask: "if the calculation of prices should be double up. if the price calculated like 89.425
  it should be 89.43" → new `Models/MoneyRounding.cs`. Money had NO rounding at all: display formatting
  (`ToString("N2")`) rounds half away from zero, so the screen looked right while the stored and summed
  figures kept full precision. Derived amounts now round at the calculation — `EmbeddedTax`, and
  `PortionTax` on both paths. **Split lines round PER LINE before summing**, because each line's tax is
  printed beside its amount; three 0.10 lines at 5% must total the 0.03 shown and not the 0.02 a
  rounded total would give. `OrderEditWindow` rounds its display copy identically or the two disagree.
  Wrong assumption caught by a red assertion: `CreateForStandardRate` taxes EVERY method, unlike
  `CreateDefault` — the code was right and my expectation was not.
- Mid-turn ask: "Fix sonarqube issues after" → ran the analyzer as a package rather than trusting the
  IDE view, which found **9 issues across 6 files**, most months old and none visible in a green build:
  S6605 ×4 (`Any` → `Exists` on `List`), S6602 ×2 (`FirstOrDefault` → `Find`/`Array.Find`), S125 ×2
  (prose comments whose trailing semicolon reads as code — reworded), S4144 (`OnFinalSplitModeChanged`
  byte-identical to `OnSplitModeChanged`; neither read `sender` or `e`, so the six balance-stage
  bindings were repointed at the one handler — stage independence lives in
  `DepositEnabled`/`FinalEnabled`, not in having two identical methods). Now **0 warnings, 0 errors**
  workspace-wide, and `Directory.Build.props` keeps the analyzer on so the gate is the build itself.

### 2026-07-30 18:40 — v4.0.3: the reason section wraps, and phone numbers punctuate themselves  [DONE]
- Ask: "Another UI fixes: 1.Cancellation / Return reason section is overflowed. make this section >Make
  whole text in this section text wrappable. 2. Add auto format for phone numbers when user type in
  phone number. for instance, in canada it should be xxx-xxx-xxxx, do the same thing for other countries
  with their own respective rules on formatting. if they don't have, then no change. 3. push as a little
  fixes on hotfix release."
  Mid-turn: "Also, for main records Custom service column. if the user has custom made service, add a
  break between Yes and services. for instance: Yes / (Qipao, Shirt)"
- Plan:
  - [x] T1 render the cancel/return reason row and find what actually overflows before changing anything
  - [x] T2 make every text in that section wrappable (label, picker rows, placeholder, messages)
  - [x] T3 per-country grouping patterns in `phone-countries.json`, keyed by DIGIT COUNT
  - [x] T4 progressive formatter in `PhoneNumberField` — caret preserved, backspace over a separator works
  - [x] T5 custom-service column onto two lines without making rows ragged
  - [x] T6 harness coverage in `phonecheck`; build, suite, render, publish, docs, README v4.0.3
- Notes: **T1/T2** rendering first was what found it, and the label was not the bug: `OrderEditWindow`
  declares its OWN keyed `FieldLabel` with no `BasedOn`, so it REPLACED the theme's and the wrapping
  added in v4.0.2 never reached this window. Theme's `FieldLabel` gained `TextWrapping`; the local one
  now says `BasedOn="{StaticResource FieldLabel}"` (same key, resolves to the merged dictionary's).
  Third time this shape has cost a session — rule written into `context.md`. Also wrapped the reason
  picker (`ItemContainerStyle` + `ContentTemplate`, `BasedOn` the theme's or it would have replaced the
  row chrome) and the placeholder, and gave the picker a `MaxWidth`. **T3/T4** `nationalFormat` keyed by
  DIGIT COUNT — Japan writes 10 and 11 differently, and 10 is genuinely ambiguous (03-1234-5678 vs
  045-123-4567) so it ships no rule and is left alone. `PhoneCountry.FormatNational` is progressive:
  a separator is emitted only when a digit follows it. Caret restored from
  `TextChangedEventArgs.Changes`, NOT `SelectionStart` — the latter differs by route (keystroke, paste,
  `Text=`) and a harness cannot fake real keys. Backspace onto a separator takes the digit in front.
  Constructor parameter made REQUIRED, not optional, so no call site can silently mean "no format".
  **The render caught what the harness could not**: typing a Japanese landline one digit at a time
  passes through 9 digits, which the 11-digit pattern groups, and the borrowed dashes were still there
  at 10 — fixed by returning bare digits for an accepted-but-unpatterned length. Lesson in `context.md`.
  **T5** the flag and garment names now stack; rows stay level because the second row is a fixed 15px
  that stands whether the names show or not, and both lines fit inside the existing `MinHeight` 54, so
  no row grew (`rowcheck` asserts uniform heights at 12px and 40px). Build 0/0; `phonecheck` 112 → 167.
  **Suite:** six assertions came up red in `taxcheck` and `shopcheck` that REPRODUCED ON A CLEAN
  CHECKOUT — nothing to do with this change. Harnesses were being run from the scratchpad, and
  `SystemSettingsPaths` probes the working directory, so every shipped preset read as absent and each
  loader degraded silently to its fallback (one phone country, one tax jurisdiction). Some harness bins
  had carried a stale copy of those files and passed by luck until an incremental build swept it away.
  `run-suite.ps1` now sets the working directory itself; `taxcheck` 0 → 323, `shopcheck` 28 → 33 on that
  alone. Two real expectation updates: `namecheck` and `shopcheck` asserted a phone string that the
  field now punctuates — same digits, and `shopcheck`'s trimming claim is subsumed by the grouping.

### 2026-07-30 18:05 — v4.0.2: a drawn checkbox, and two labels that would not wrap  [DONE]
- Ask: "UI improvements: Do another hotfix version: >Add checkbox redesigning based on the main theme.
  >the price breakdown labeling section should be a bit wider, make text wrappable, right now the text
  is overflowed. >Basic information label is overflowed, make it veritcally middle, but wrappable."
- Plan:
  - [x] T1 `ThemedCheckBox` in `AppTheme.xaml` — keyed AND implicit, matching `ThemedRadioButton`
  - [x] T2 breakdown label column wider + wrappable
  - [x] T3 basic-info labels vertically centred + wrappable
  - [x] Build, render, suite, publish, CJK sweep, docs
- Notes: **T1** drawn square with a `Path` tick, a hover halo outside the box (so hover never reads as
  half-ticked), an indeterminate dash, keyboard focus, and a disabled state. **Checked + disabled keeps
  its fill** — a locked "deposit received" must still read as received, and the stock grey-out says the
  opposite of what is true. `NotApplicableCheckBox` carries its own template and was left alone.
  **T2/T3 had ONE cause and it was not the labels**: a horizontal `StackPanel` measures children at
  infinite width, so `TextWrapping` on a `TextBlock` inside one does nothing. The 8 basic-info label
  blocks became `DockPanel` + `DockPanel.Dock="Left"` icon + `VerticalAlignment="Center"`, which is what
  makes both halves of that ask work at once. `FieldLabel` gained `TextWrapping="Wrap"`. The breakdown
  labels were the other case — wrapping was live, the column was just too narrow: 18 columns `120`→`158`,
  48 labels wrapped. Self-inflicted regex over-match left DockPanel tags 5-open/8-closed; found by
  pairing every `</DockPanel>` to its opener. Build 0 warnings / 0 errors; suite 1530/0 across 25
  harnesses; rendered `checkboxes.png` (all five states) and `editor.png` (both label blocks) — a
  template is the one thing an assertion cannot judge. Lesson in `context.md`.

### 2026-07-30 17:20 — A drawn radio, and the custom-made window on the theme  [DONE]
- Ask: "UI improvements: >Redsign the radio group themem globally. make a new look and feel for radios.
  >Apply the main theme to Custom made record view(especially the input fields). the styles is not
  matching the global theme"
- Notes: `ThemedRadioButton` in `AppTheme.xaml` — a drawn template replacing the stock grey ring: an
  18px ring that answers the pointer (soft halo OUTSIDE the ring, so hover never reads as half-selected),
  a brand dot and a bolded label on selection, a real disabled state, and a transparent root so the whole
  row is the hit target rather than the ring alone. Keyed AND implicit, matching `ThemedTextBox`.
  **The keyed variants had to be found and rebased or they would have kept the stock look while
  everything else changed** — `MethodRadio` (42 radios) and `ShopSetupWindow.ModeRadioStyle` now say
  `BasedOn`; `FilterChip` and `ChallengeBox` carry their own templates and were left alone.
  `CustomMadeServiceWindow` declared a local implicit `<Style TargetType="TextBox">` with no `BasedOn`,
  which REPLACES the theme's rather than extending it — that one line is why every input in that window
  looked wrong. Fixed, plus three inline `Padding="6,4"` overrides removed and all 16 hard-coded hex
  values mapped to the palette. Both lessons in `context.md`.
  Verified by RENDERING both — a template is the one thing an assertion cannot judge; the window
  compiled and behaved correctly the entire time it looked wrong.

### 2026-07-30 15:10 — v4.0.1 hotfix: the split allocates itself  [DONE]
- Ask: "Hotfix for v 4.0 > Bug Fix - the Skip deposit will be the same as Pre-tax deposit as $0 -
  Issue: Missing break down for final balance stage. - The toggle on the split and non split payment
  action, the UI should work on the final balance, and displayed under the section. > Improvements:
  Make sure the total allocations shoulnd't over than the pretax- total amount. >For input in any
  fields, for example, i have total 600, I type 200 in cash, the other fields automatically applies
  400 as place holder, >If i then activate in another field, 400 should automatically inputed(the
  automatically inputed text should be editable. ), the rest becomes 0 >Also i can change to 300, then
  the rest fields with place holder can be changed to 100. >The downpayment received checkbox cannot
  be activated until all fields are equally balanced >If the amount i entered is over than 600, show
  error message that total amount should not over than 600 >Display 应收after each inputed payment
  type. for instance. 13% tax ${tax}，应收${currentpaymentamount + tax}"
- Plan:
  - [ ] Skip deposit zeroes the split rows too, not only the deposit box
  - [ ] The final stage shows its breakdown AND its own split toggle, under that section
  - [ ] Remaining-amount PLACEHOLDER in every empty row; focusing one commits it and zeroes the rest
  - [ ] Over-allocation refused with a message naming the target
  - [ ] "Deposit received" stays disabled until the stage balances
  - [x] Each row states its tax and what is receivable for that line
- Notes: `SplitRow` gained a `Placeholder` TextBlock overlaid on the amount box (the status-reason
  field's pattern — a TextBox has no placeholder, and a hint beside it reads as a typed value) and the
  tax cell became a `Detail` cell carrying rate, tax and the receivable. `OnSplitAmountFocused` commits
  the remainder and zeroes the other BLANK rows — blank and 0 are deliberately different states, a typed
  0 being an answer that must not be overwritten. Keys `OrderEdit.Split.RowTax` → `.RowDetail` (3
  placeholders) and `.Over` → `.Exceeds` (names the ceiling, which is what the user acts on) in five
  languages. Final-stage toggle added per section, mirrored onto the deposit pair, which stays the one
  the flag is read from.
  **The gate was written in the wrong place first**: `ApplySectionLock` owns
  `DownCompletedCheck.IsEnabled` and assigns it unconditionally, so a gate applied during the refresh
  pass was silently overwritten — it is now the predicate `IsSplitDepositBalanced` that method consults
  (`context.md`). Caught by four failing assertions in `splitcheck`, which grew an EDITOR section for
  this hotfix: placeholders, focus-fill, editability, the shortfall, the ceiling, the per-row
  receivable and skip-deposit are all control state, none of them reachable from the model.
  Build 0 warnings / 0 errors; `splitcheck` 44 assertions green.
- Follow-up ask: "The edit of any of the fields should reset the placeholder of the pre-taxed amount -
  the inputed amount. for the other fields that didn't entered, should be the placeholder having the
  balance. ... but for exsited fields with value, should not touch. >autovalidation on allocation
  amount should be monitor user input realtime."
  This CORRECTS the earlier "the rest becomes 0": the other rows must stay EMPTY and re-offer the
  balance, not be answered with a zero. Zeroing removed — the auto-fill now writes only the row the user
  is in. `splitcheck` grew the walk the request describes (600 offered everywhere → cash 200 → 400
  offered → debit 300 → 100 offered in BOTH remaining rows, cash untouched) plus two keystroke-level
  assertions that the tick flips as it is typed. 53 assertions.
- Follow-up ask (zh): "所有的付款方式，不拆分方式始终为预选项。现在的情况是尾款预选项没有选择。另外，我如果交付的钱多了，
  连个地方要改。首先wording：还有xxx未分配到付款方式比较难理解。应该是您多付了XXX，第二，以上确认后应该自动把最后一个
  update的位置改为剩余最大值。" + "拆分payment应该独立于各自的payment stage，目前change radiobutton 已经互相影响了"
  Four things: (1) the FINAL toggle opened with neither option chosen — only the deposit pair got a
  default; both are set now. (2) One save message served both directions, so allocating 1200 against 600
  said "600 is not allocated to a payment type" — split into `SplitUnbalanced` / `SplitOverpaid`, and
  the live line now names the overshoot rather than the ceiling (the line above already shows the
  target). (3) Leaving an over-allocated row clamps it to the remaining maximum — on LostFocus, not per
  keystroke, which would rewrite "900" at the first digit. (4) **The two stages are now INDEPENDENT**:
  `SectionPaymentSplit.Enabled` became `DepositEnabled` + `FinalEnabled`, `IsSplit` became
  `IsDepositSplit`/`IsFinalSplit`/`IsSplitAt(stage)`, and the toggle mirroring (`SyncSplitToggles`) is
  gone. Mirroring them meant choosing to split a BALANCE re-shaped a deposit already taken. JSON shape
  changed with it — safe only because no order has been saved with a split outside this uncommitted
  work. `splitcheck` at 60 assertions.
- Follow-up ask: "The lock of split paymanet is missing while the checkbox for each pricing section
  stage is ticked." — correct: `ApplySectionLock` never touched the split rows or their toggles, so a
  received deposit, a settled section and a READ-ONLY order all left the allocation editable. New
  `SetSplitStageEnabled(c, stage, enabled)` applies the locks the single-method controls already had,
  per stage: the deposit's composition freezes on `depositMethodLocked`, the balance's on
  `sectionLocked`. Asserted both ways in `splitcheck` — including that re-opening the section RELEASES
  them, since a lock that can only lock strands the control (the comment two lines above it in that
  method says exactly this about `DownpaymentBox`). 68 assertions.
- Sonar follow-up: `OnSplitAmountFocused` tripped S3776 (~17). The cause was SEARCHING and ACTING
  interleaved — two nested loops looking for the row that owns the focused box, with the guards and the
  fill inside them. Split into `FindSplitSlot` (returns a `SplitSlot` record struct) plus a flat
  handler: ~4 branch points, and neither half is complicated. The rest of the hotfix methods were
  audited the same way and are all ≤ 8.

### 2026-07-30 13:05 — v4.0: one stage, several payment types  [DONE]
- Ask: "now we are gonna doing a Major release for V4.0, it will be related to split on Payment
  section. Some customers they may pay differnt payment type in deposit stage and final payment stage.
  >only applies to tax appied extra countries, like Canada and US >Provide an option to split the
  payment by types. by default, no split, which follow the same structure as we had (no split
  payments->this is the default or Split payments) >when click Split payments after,For instance, as a
  customer, I want to pay total of 600 pre-taxed deposit -allow user to fill different amount of money
  for different payement type -lets say I pay 400 cash, and 200 by card (if cash is 0%, 13% for card).
  then the tax rate should only apply on the card automatically -also I can select 200 cash, 200 card
  and 200 for the etransfer. >On final payment step, Follow the same logic >Even I can choose all types
  of the payments. Note, the UI may need adjustment, think of a good UI to handle this situation.
  >Update none selection. Update currently None be more descriptive, lets call it (Skip deposit)"
- Decisions taken from the user before building:
  - **Fixed rows, one per method**, each with an amount box and its own tax shown, plus a running
    "left to allocate". No add/remove list state, and the same shape serves both stages.
  - **The toggle is PER SECTION**, like every other payment setting on this form, and covers both
    stages of that section.
  - **The typed deposit stays the target** and the lines must add up to it — which is also the only
    shape the final stage can have, since its target is subtotal − deposit and cannot be typed.
  - **A final stage that does not add up is refused at save.** A shortfall would be a partial payment,
    and there is no such state anywhere in the model, the receipt or the balance column.
  - Tax-EXCLUSIVE locations only, as asked: where the price already contains the tax there is nothing
    for a per-method split to change.
- Plan:
  - [ ] `Models/PaymentSplit` — one line (method, amount, frozen rate) + a section's two stages
  - [ ] `Order.PaymentSplitsJson` (one column, runtime guard) + per-section accessors
  - [ ] `CalculateSectionPayment` takes an INPUT STRUCT so the split cannot be forgotten by a call
        site (S107, and the lesson from the pricing-mode flag: never an optional parameter)
  - [ ] Per-portion tax = Σ line × that line's rate; no-split stays the one-line case
  - [ ] UI: toggle + split rows per section, live allocation, in the three payment cards
  - [ ] Validation: both stages must balance; message names the shortfall
  - [ ] `PaymentMethod.None` → "Skip deposit" in five languages (NOT Alteration.Category.None)
  - [x] Harness, suite, README v4.0.0
- Notes: New — `Models/PaymentSplit.cs` (line / section / order), `SectionPaymentInput`,
  `Order.PaymentSplitsJson` + `PaymentSplits` + `SetPaymentSplits`, `Order.PortionTax`, the column
  guard in `App.xaml.cs`. `OrderEditWindow`: per-section toggle, split rows BUILT in code from
  `PaymentTaxRules.ConfigurableMethods` (three sections × two stages × four methods is twenty-four
  rows nobody wants in markup), live per-row tax and allocation summary, `ApplySplitModeVisibility`
  split out of `UpdateSectionVisibility` to keep S3776 under 15, save/load, and
  `ValidateSplitAllocations`. Eight keys × five languages; `PaymentMethod.None` → "Skip deposit"
  (`Alteration.Category.None` deliberately untouched — a different "None").
  Build 0 warnings / 0 errors. New `splitcheck` (24 assertions) covers the asked example, three- and
  four-way splits, the final stage, one-line-split == unsplit, a legacy null column, the shop's rules
  still governing per line, tax-inclusive ignoring the split, and the JSON round trip.
  A UTF-8 BOM was introduced into all five language files by a PowerShell rewrite and stripped again —
  `Set-Content -Encoding UTF8` writes a BOM in PS 5.1; use `UTF8Encoding($false)`.
  **The suite caught a crash the build could not**: the toggle shipped with `IsChecked="True"` in
  markup, which raises Checked during `InitializeComponent`, before the section controls exist — every
  order window died on open with an NRE. Default moved into code and the handler guarded with
  `_sectionsReady` (`_syncingPayment` is false at parse time and does not cover it). Also: the live
  database needed `scratchpad/livemigrate` to gain `PaymentSplitsJson` before any harness reading it
  could run, and `balancecheck`/`taxcheck` were converted to the new input struct through a local
  `Money(...)` helper that keeps the original parameter names so their assertions read unchanged.
- Follow-ups NOT done, and deliberately: the receipt and the order detail panel still print the single
  stored method per portion, so a split order's paperwork names one method rather than listing what was
  taken by each. The money on them is right — it comes from `SectionPayment` — but the narrative line
  is not yet split-aware.

### 2026-07-30 11:40 — A phone number carries the country it belongs to  [DONE]
- Ask: "New feature: Update phone number validation based on store's location. the internation code
  should show up front. with a dropbox with flag and internation code. by default, use the selected
  currency as the default international code and validation."
- Follow-up ask: "it impacts the new order, but not the exsiting order. meanwhile you can update the
  DB mocking data based on the stores selection. For demo purpose"
- Decisions taken from the user before building:
  - **Default from LOCATION, currency only as the fallback.** The ask said both; they disagree for a
    Spanish shop, because the currency→location table maps EUR to France. Location is never worse.
  - **Flags drawn in XAML, not emoji.** Windows ships no country-flag glyphs — Segoe UI Emoji renders
    a regional-indicator pair as the two LETTERS. Emoji here would have shipped "CA" in a box.
  - **National length per country** (CA/US 10, CN 11, JP 10–11, FR 9, ES 9), keyed on the country
    picked for THAT number rather than on the store, so a Canadian shop can still take a Chinese
    customer's mobile.
  - **All five phone fields**, through one control — they already share one validator, and a second
    rule would be free to drift.
  - **New records only.** An existing order keeps the loose rule, so nothing already saved starts
    refusing to save over a phone number typed before the rule existed.
- Plan:
  - [ ] `Settings/System/Defaults/phone-countries.json` — code, dial code, national digit lengths.
        Its OWN file: a phone rule is not a tax preset, and the two lists need not stay the same length
  - [ ] `Models/PhoneCountry` + `Services/PhoneCountries` — shaped after TaxJurisdiction(s)
  - [ ] `ContactValidation.IsValidPhone(phone, country)` beside the loose one, which stays for
        legacy records
  - [ ] `Themes/Flags.xaml` — six vector flags as `DrawingImage`
  - [ ] `Views/Controls/PhoneNumberBox` — flag + dial-code picker in front of the number box
  - [ ] Wire into OrderEdit, CustomMadeService, ShopSetup, StoreMembers, UserManagement
  - [x] Keys ×5 languages; demo data refreshed per store location (BACKUP first)
- Notes: New — `phone-countries.json`, `Models/PhoneCountry`, `Services/PhoneCountries`,
  `Themes/Flags.xaml` (6 `DrawingImage`), `Controls/PhoneNumberField`. Changed —
  `ContactValidation.IsValidNationalPhone` beside the loose rule, `SystemSettingsPaths`,
  `AppTheme.xaml` (nests Flags by ABSOLUTE pack URI), and the five windows. Eight keys × five
  languages (`Country.*`, `OrderEdit.Validate.PhoneDigits`); `OrderEdit.Validate.PhoneInvalid` reused
  rather than duplicated.
  `RequiredTextField` now holds `Func<bool> IsBlank` + `Action Focus` with a `For(TextBox…)` factory,
  so the phone stays inside the one-pass required check that reports two missing fields as two.
  Demo data: `scratchpad/phoneseed` (dry-run by default), backup written to
  `orders.db.bak-20260730-113900`, 193 rows — shops whose number already fits their country were KEPT
  and only had the dial code normalised onto the front.
  Build 0 warnings / 0 errors. New `phonecheck` harness (112 assertions) added to `run-suite.ps1`;
  `validcheck`, `balancecheck`, `namecheck` and `shopcheck` repointed at the new field, and
  `formatcheck` gained (key, language) cognate exemptions for `Country.CN` in es-ES and
  `Country.CA`/`Country.FR` in fr-FR — those names ARE the same word in those languages.
  **`shopcheck` caught a real app bug, not a rename**: `SystemSettingsPaths` probed for the FOLDER
  `Settings/System` rather than for the file wanted, so a root holding a partial copy won the probe and
  the missing files degraded silently to built-in fallbacks — a stored "+86 …" then matched no dial
  code and rendered as `+1 +86 20 1234 5678`. Now probed per file. Flags verified by rendering them
  (`scratchpad/flagshot`), since "has a flag resource" passes just as happily for a blot. Six S2325 flags on the control are the documented
  standalone-analysis false positive (the full build, which sees the generated `x:Name` partial,
  reports none) and are resolved with one justified class-level `[SuppressMessage]`.
  CJK sweep clean. Lessons in `context.md`, components in `Architecture.md`.
- Known, reported not fixed: shop #10 "Shanghai LeeYonge Bespoke" is located `CA-BC`, so it seeded
  Canadian numbers. Changing a shop's LOCATION also changes its tax treatment, which is the user's
  call and not a phone-number task.

### 2026-07-30 10:06 — A tax-inclusive order explains itself in its own words  [DONE]
- Ask: "改变计税的breakdown， 当服务已经包含税的时候我们的wording需要更改。 对于已经选择了相应国家的地区，
  此时计税方式也需要一定变化。税前服务总价标签要改为服务总价（含税），定金税率和尾款税率不再发生变化，那些含税的地区消费税
  或者增值税是一致的。然后 在定金阶段就不需要给出price breakdown了。在尾款支付阶段， 只需要给出这几个， 比如中国：1.
  服务总价（含税），已收定金，应收尾款，和剩余尾款。 然后就说明一下，已含增值税（6%） 多少多少。看看我说的还有哪些遗漏的 再做"
- Gaps found in the ask and settled with the user before building:
  - **The tax has a NAME, and it is not the same name everywhere.** The ask says 增值税 because the
    example is China; Japan's is a consumption tax and the EU's is VAT. Only the generic
    `Order.Fields.IncludedTax` existed. Decision: a jurisdiction DECLARES its tax name, exactly as it
    already declares its tax number — new `taxNameLabel` → `TaxName.<name>` key.
  - **A cleared final balance would state nothing it was paid.** The four requested rows leave
    `Order.Fields.FinalBalance` at zero and no row saying what came in. Decision: show
    `Order.Fields.ReceivedFinalBalance` as a fifth row when it is non-zero.
  - **The rate box.** Kept, with the stage dropped from its label (there is one rate), because the
    deposit stage now has no breakdown at all and this is the only place the rate appears there.
  - **The receipt and the detail panel have the same defect** — one bare `价内税额` line, no rate, no
    name. Decision: they follow the same wording.
  - Also carried, unasked but the same bug: `Order.Fields.PreTaxDownpayment` labels a deposit that is
    likewise tax-inclusive here.
- Plan:
  - [ ] `taxNameLabel` in the shipped presets + `TaxJurisdiction.TaxNameLabel/TaxNameKey/TaxName()`,
        `TaxJurisdictions.TaxName(shop, loc)`, defaulting to a generic name where none is declared
  - [ ] Keys ×5 languages: `TaxName.{Vat,ConsumptionTax,GstHst,SalesTax,Generic}`,
        `Order.Fields.{InclusiveServiceTotal,InclusiveDownpayment,IncludedTaxLabel,IncludedTaxRateLabel}`
  - [ ] `Order.IncludedTaxRatePercent` — the rate a saved inclusive order quotes
  - [ ] `TaxLabelConverter` takes the order + a section, so one label serves screen, panel and receipt
  - [ ] `OrderEditWindow`: mode-dependent price/deposit labels, stage-free rate label, deposit
        breakdown collapsed, and a compact inclusive final panel per section (4 rows + note)
  - [x] `MainWindow` detail rows + `AddReceiptTotals` line
  - [x] `taxcheck`: repair for the Canada collapse, then cover the inclusive panel
- Notes: `tax-jurisdictions.json` (+`taxNameLabel` on CN/JP/FR/ES and a `taxNameComment`),
  `TaxJurisdiction` (`TaxNameLabel`/`TaxNameKey`/`TaxName`), `TaxJurisdictions.TaxName`,
  `Order.IncludedTaxRatePercent`, `TaxLabelConverter` (rewritten: takes the order, `static Label`),
  `MainWindow.xaml` detail row + `AddReceiptTotals`, `OrderEditWindow.xaml`/`.cs` (per section: named
  price/deposit labels, `FinalInclusivePanel`, 11 new `PaymentSectionControls` members;
  `UpdateInclusiveBreakdown`, mode-aware `UpdateTaxLabel`, `UpdateSectionVisibility(c, pricesIncludeTax)`),
  seven keys × five languages. Build 0 warnings / 0 errors. `taxcheck` 323 assertions green, including a
  new section that drives the ORDER WINDOW in both modes; suite 1346 passed / 2 failed, both in
  `langcheck` and both pre-existing live-DATA drift — no shop in the live database installs ja-JP, so
  "at least one shop installs every shipped language" cannot pass (see the entry below on adding a
  language being a data task). Not touched: it is the user's own shop configuration.
  CJK sweep clean over code + shipped data. Lessons in `context.md`, component map in `Architecture.md`.

### 2026-07-30 09:30 — Canada: sales tax added separately, and one entry rather than three  [DONE]
- Ask: "Make Canada sales tax added separately.  the same as US" → then "Do not split Canada by
  regions, but keep the exsiting functions to detect country regions for tax rate"
- Notes: `CA-ON`/`CA-AB`/`CA-BC` (13/5/12%) collapsed to one `CA` at `standardRatePercent: 0`, so a
  Canadian shop is seeded TAX FREE and enters the rate it collects — the same treatment the US entry
  already had, and the display name now says so in all five languages. `DefaultCode` and the built-in
  fallback moved with it. The region machinery is deliberately intact: codes stay free-form, the
  `TaxJurisdiction.CA-*` label keys stay in every language file marked dormant, and
  **`TaxJurisdictions.For` now widens an unshipped regional code to its country** (`CA-ON` → `CA`,
  `US-CA` → `US`, `JP-13` → `JP`) before falling back to the home market — without it every shop
  already stored under a provincial code landed on the home market by luck. `Find` stays strict.
  No migration rewrites a stored code, so a re-added province takes effect by itself. Verified against
  the shipped file with a throwaway probe; `taxcheck` went red on the collapse and is repaired as part
  of the entry above. Build 0 warnings, publish refreshed.

### 2026-07-30 03:10 — Store Management: delete / delist / download / restore / reinitialize  [DONE]
- Ask: "UI Improve: Enlarge Select Shop panel. Make sure it has enough room for more buttons listed
  below for admin rights. Add new feature for admin right called Store Management, it will requires a
  new panel: admin user is now able to perform the following operations in Store Management stage: 1.
  In the list of shops, can perform Delete the shop action. 2. admin user can perform reinitalize the
  whole application. Since both actions are really sensitive, only admin can do the operations. to
  prevent unawared flaw action, you need to pop up another panel. in the panel, pops up a message to
  ask user type a random created text length of 10, if the user type them all correctly, it means a
  dedicated action. But still, the pop up can provide two buttons, save the store records and remove
  the store now. buttons will be disable if you don't type the above text correctly. in the Store
  Management you will have the right to restore a single store or multiple stores or even whole stores.
  ctrl+left click can select multiple stores. Use the main theme UI to do this. -In selected stores,
  you can try delete all or download all data -panel provides the restore options for stores.
  -deletion on stores will have validations prevent user unnoticed action."
- Follow-up ask: "Also add a feature to delist/activate the stores for admin to perform"
- Decisions taken from the user before building, because both change the design materially:
  - **Restore source = user-picked files.** "Save the store records" opens Save-As; Restore opens a
    file dialog. Nothing is archived inside the app, so the panel does not list restorable stores.
  - **Reinitialize keeps accounts.** Shops, orders and per-shop files go; `credentials.json`, the
    language preference and global settings survive, so nobody is locked out and the admin lands on
    the create-first-shop path.
- Plan:
  - [ ] `Shop.DelistedOnUtc` (+ `IsDelisted`), mapped, with a runtime column guard and CREATE TABLE
        entry. Records WHEN, like `ShopMembership.DeactivatedOn` already does
  - [ ] `Services/ShopArchive` — export selected shops (rows + per-shop files + attached documents) to
        one zip; `TryRead` validates with no side effects; `Import` restores, keyed on `PublicId`
  - [ ] `Services/ShopAdministration` — the one place the destructive rules live: delete, delist,
        activate, reinitialize. Cross-shop, so it must `IgnoreQueryFilters`
  - [ ] `Views/ConfirmDestructiveWindow` — random 10-character challenge; the two buttons stay
        disabled until it matches exactly
  - [ ] `Views/StoreManagementWindow` — `SelectionMode="Extended"` (ctrl/shift click), on the shared
        `ManagementStyles` so it matches Select Shop / User Management / Store Members
  - [x] `ShopPickerWindow` enlarged (820×640 → 1000×740) + Store Management and Create-demo-store
        buttons. Delisted shops needed NO picker change — see the first note below
  - [x] Localization ×5 (616 keys, exact parity); `scratchpad/storecheck` (50 assertions); docs
- Later ask, same turn: "one click on the Select store panel, to create a demo store … saved in the
  default" → `ShopAdministration.CreateDemoShop`, built from the shipped presets (home jurisdiction,
  its currency, tax rules seeded from its standard rate, names in every installed language). No orders
  fabricated: inventing customer records inside a real installation is a different thing from giving
  somebody somewhere to start.
- Notes — four findings, each of which cost a wrong first move:
  1. **`Shop.IsArchived` already existed and already meant "delisted"** ("hidden from the shop picker
     without deleting its orders"), already honoured by the startup shop load, the picker and the
     shop-name uniqueness check — with no UI anywhere to set it. I designed a parallel `DelistedOnUtc`
     flag before grepping for prior art. Corrected: the bool stays authoritative, `IsDelisted`
     delegates to it, and `DelistedOnUtc` is the audit stamp beside it. Delisting therefore works
     everywhere without touching those three call sites.
  2. **A restore would have silently re-parented every order.** `AppDbContext.StampNewOrdersWithShop`
     overwrites `ShopId`, `CurrencyType` and `PricesIncludeTax` on every ADDED order from whichever
     shop is open — right for every other call site, catastrophic for an importer that already knows
     all three. Added `SuppressShopStamping()`, an explicit `using` scope, deliberately awkward.
  3. **Deletion had to reach outside the database.** Per-shop files are named after `PublicId`, so a
     file outliving its shop eventually hands a NEW shop an old one's terms or branding.
     `MeasurementTermsService.FilePathFor` / `ReceiptBrandingStore.DirectoryFor` were exposed from the
     services that own those naming rules rather than the rules being copied.
  4. **The harness gap I flagged is exactly where the bug landed.** Shipped with no coverage; the first
     export threw `System.Text.Json.JsonException: a possible object cycle was detected`, because EF's
     relationship fix-up populates `OrderItem.Order` and nothing had ever serialized an order before.
- Follow-up bugs reported by the user and fixed:
  - JSON cycle → `[JsonIgnore]` on `OrderItem.Order`, at the source rather than
    `ReferenceHandler.IgnoreCycles` at the one call site that hit it.
  - Challenge phrase uncopyable and low-contrast → own template (the theme's `IsReadOnly` trigger was
    repainting the chrome; see `context.md`), explicit Copy button, visible selection brush.
- Final: build 0 warnings / 0 errors, no Sonar findings, suite **1377 passed / 0 failed across 23
  harnesses**, 616 keys × 5 exact parity. Live DB backed up to `orders.db.bak-preStoreManagement`.

### 2026-07-30 02:40 — Chinese-comment sweep: the check was narrower than the rule  [DONE]
- Ask: "Checking if there is Chinese comments inside the project. if yes, just replace with Chinese
  comments" — then "Just replace with english" / "this is a typo".
- Finding: `.cs`/`.xaml` were clean. The **companions were not** — ~310 lines of Chinese UI labels
  across `Architecture.md`, `context.md`, `TODO.md`, `RefinedTODO.md`. Root cause: the rule covers
  Markdown explicitly, but the verification grep in `SKILL.md` globbed only `*.cs,*.xaml`, so it
  reported clean on every run. See `context.md`, "a check narrower than the rule it checks".
- Plan:
  - [x] Widen the sweep to `*.md,*.json,*.csproj,*.ps1`; make the character class explicit (a first
        attempt including curly quotes matched every fr-FR line, since French `l’` is U+2019)
  - [x] Resolve each Chinese label to its key + English value FROM the language files rather than by
        hand, and drive the rewrite from a UTF-8 JSON side-file (PowerShell 5.1 mojibakes non-ASCII
        literals in a BOM-less `.ps1`)
  - [x] Preview before applying — which is what exposed that a bare-token pass mangles compounds, and
        that many hits are value-RENAME records, i.e. the sanctioned case
  - [x] Three passes: label map (132 lines), then context-qualified phrases for `context.md` (16) and
        `TODO.md` (24); the rest hand-edited
  - [x] `SKILL.md` grep widened + a paragraph on how to triage hits; `SkillUpdates.md` entry
- Notes: docs backed up to `scratchpad/cjksweep/backup` first. Remaining CJK is all **sanctioned** by
  `SKILL.md`'s three exceptions, verified rather than assumed: every one of the 29 UI labels still in
  `TODO.md` sits inside a verbatim `- Ask:` quote (checked by looking for the quote character, not the
  line prefix); the rest is punctuation being described, the `` `Key` (value) `` form, rename records,
  rendered-output examples, shop-name values, and the name-split example. Source: 0. No code touched,
  so no build or suite impact.

### 2026-07-30 01:20 — One validation path: banner + inline message + popup  [DONE]
- Ask: ">Improve user experience. When new order / modify existing user, the missing of customer name
  and mobile number should flag red error message on the top section. - Now modualrize the error
  message box. If an input box is not allowed empty, first it pops up an error message, and secondly,
  it also metioning the error message in respective place under the input. e.g. 电话号码不能为空 ，
  this message should be appear under the customer phone area. 请输入客户名称 ， this message should be
  appear under the customer name area. same as other fields, I can remember email and return/cancel
  reasons. may have the validation."
- Starting state, which is the problem: every check sets `ErrorText`, which is at the BOTTOM of the
  form beside Save; only email and phone have an inline message, and only for the FORMAT check, not
  the empty one; and the popup fires for 5 of the 11 checks. Customer name has neither.
- Plan:
  - [x] `ValidationBanner` under the title and OUTSIDE the `ScrollViewer`; `ErrorText` at the foot keeps
        only save FAILURES (exceptions)
  - [x] `Fail(key, inline, focus)` + `TryRequireFilled(fields)` are the only reporters;
        `_validationProblems` is what the banner and the dialog both read
  - [x] Inline blocks for order number, customer name, address, status-reason category and text (email
        and phone already had them), all from one shared `FieldErrorText` style
  - [x] Required-empty fields collected in ONE pass so name AND phone are both flagged
  - [x] `RegisterValidationClearing` — typing clears that field's own message; reason messages cleared
        when the row hides, and the category's when a category is picked
  - [x] `WarnIfEmailMissing` (e-transfer) and `CanOpenCustomMadeWindow` routed through the same path —
        both previously raised a dialog and left the form looking untouched
  - [x] Dialog split into `TryValidateForSave` (announces) / `ValidateForSave` (marks) so the marking
        half is drivable; a `MessageBox` inside a check blocks the thread and hangs the harness
  - [x] New `scratchpad/validcheck` (41 assertions); suite runner now 22 harnesses
- Notes: no new string-table keys — all 12 messages already existed in five languages, and `validcheck`
  asserts that. One of my own assertions was wrong first: "the error block shares a parent with its
  input" is false for the reason box, whose `TextBox` sits in a `Grid` so the placeholder can overlay
  it; restated as "same container, after it". Final: build 0 warnings / 0 errors, no Sonar flags on the
  changed files, suite **1327 passed / 0 failed across 22 harnesses**, 0 CJK in `.cs`/`.xaml`.
  Uncommitted along with the v3.0.1 currency-panel fix.

### 2026-07-30 00:05 — Bug: the currency picker offered a currency with no tick box  [DONE]
- Ask: "Currently I have English and Francais language selected. the corresponding list of all
  avaialbe currencies symbols are correctly displayed. -Issue: the dropdown didn't display correctly,
  but keep JPY in the record. -Request: Find the reason and fix the issue." + ">Add a preventer, make
  sure at least one currency must select for the store. show error message prevent in red text(no pop
  up alert), then force auto select the first symbol as default"
- Clarified mid-turn: "we should not have JPY in the dropdown record."
- Reason, confirmed from the live database rather than reasoned about: shop #1 "Shanghai LeeYonge
  Bespoke" stored `SupportedCurrenciesJson = ["CAD","JPY"]` against
  `InstalledLanguagesJson = ["en-US","fr-FR"]`. `BuildCurrencyRows` seeds a row per currency the
  SYSTEM's languages offer (CAD USD CNY EUR JPY) plus anything the shop accepted, while
  `RefreshCurrencyGroups` groups cards by the SHOP's ticked languages (English → CAD USD,
  Français → EUR). JPY therefore had a ticked row in no card at all: invisible, offered in the picker,
  and written back on save with nothing on screen able to remove it.
- Plan:
  - [x] New `OfferedByTickedLanguages()` — what the right pane actually shows, in offer order
  - [x] `TickedCurrencies()` scoped to it, so the panel returns exactly what it shows. Replaces the
        earlier "keep an orphaned currency" rule — right intent, wrong mechanism (see `context.md`)
  - [x] `EnsureOneCurrency()` — live floor guard: red inline text + re-tick the first offered
        currency, run on every toggle AND in the constructor, `_repairing` guard against reentrancy
  - [x] Error line cleared as soon as the state is valid (inlined, not a helper — S2325 false positive)
  - [x] `currencycheck` gained `RunCurrencyWithoutALanguage` using the real shop's stored state as the
        fixture; the "refused on Done" assertions rewritten for the repair-on-toggle behaviour
  - [x] Fixed 3 unrelated red assertions in `currencycheck`: the backfill fixture SEARCHED live data
        for a non-CAD shop and the user had since switched that shop to CAD. It now establishes the
        state, and picks the shop with the most orders so the precondition cannot go vacuous
  - [x] Same drift had made a `taxcheck` assertion go QUIET rather than red — `if (jpyShops > 0)`
        skipped and the tally slipped 351 → 350 with nothing pointing at it. The yen shop is now
        established and the assertion unconditional; recorded in `context.md` as the worse failure mode
  - [x] When no language is ticked the red line now names the missing LANGUAGE rather than leaving a
        currency message describing a state that is no longer the problem
- Notes: build 0/0. Shop #1's stored `["CAD","JPY"]` is corrected the next time the panel is opened
  and Done pressed — the panel now returns `["CAD"]`. No order moves: an order records the currency it
  was priced in and never reads the shop's. Not force-migrated in the database without being asked.

### 2026-07-29 23:10 — Tax number named by the jurisdiction, asked for only where one is issued  [DONE]
- Ask: "更改一下wording, 现在称为GST/HST 这种针对加拿大的版本。 如果当地区有针对消费税的才需要给出税号，否则可以不用给。你觉得这种改动合理么"
- Follow-up ask: "如果改动合理，那么请继续改动，改动完提交到main 请更新相关日志。"
- Assessment given: reasonable, and the same defect as the rate one level down — `"GST/HST"` was
  spelled into 15 string-table values, so a shop in Osaka read `税番号（GST/HST）` and printed
  `GST/HST 番号：…` on its own tax slip. One refinement recommended and applied: gate on the
  jurisdiction DECLARING a tax number rather than on `pricesIncludeTax`, because Canada's GST/HST is a
  consumption tax quoted tax-exclusive, so inferring would drop the home market. Field kept optional
  rather than mandatory — a newly registered shop may not have a number yet.
- Plan:
  - [x] `taxNumberLabel` per jurisdiction in `tax-jurisdictions.json` (omitted for US), grouped by tax
        REGIME: `GstHst` (CA-ON/AB/BC), `Vat` (FR/ES), `ChinaTaxpayer`, `JapanInvoice`
  - [x] `TaxJurisdiction.TaxNumberLabel` / `.CollectsTaxNumber` / `.TaxNumberKey` / `.TaxNumberName`;
        `TaxJurisdictions.CollectsTaxNumber` / `.TaxNumberName` / `.TaxNumberKey`
  - [x] 5 new `TaxNumber.*` keys × 5 languages; `Shop.Setup.TaxNumber` and `Branding.TaxNumber`
        PRUNED (orphaned once the label came from the jurisdiction); `Receipt.TaxNumberLine` →
        `{0}: {1}` with per-language punctuation; both hints reworded
  - [x] `ShopSetupWindow` — `TaxNumberPanel` hides where none is issued, `TaxNumberLabel` set from the
        jurisdiction, both driven live from `ApplyLocationMode`
  - [x] `ReceiptBrandingWindow` — `BuildCardTitled` overload so the card title can come from the
        jurisdiction rather than a fixed key; card stays visible (it edits the OVERRIDE)
  - [x] `MainWindow.CreateTaxNumberBlock` + `ShopLetterhead` pass the name into the line
  - [x] `formatcheck` exempts `Receipt.TaxNumberLine` (now pure punctuation, identical en/es);
        `taxcheck` gained `RunTaxNumberNaming` + settings-screen assertions
- Notes: 562 keys × 5 languages, exact parity. The US-location assertion is driven on the NEW-shop
  window because a new shop is never asked before reseeding — on an existing shop it would raise the
  confirmation dialog and stall the harness, the same trap as earlier today. Final state: build
  0 warnings / 0 errors, suite **1272 passed / 0 failed across 21 harnesses** (taxcheck 253 → 351),
  0 CJK in `.cs`/`.xaml`. Committed to `main`.

### 2026-07-29 21:40 — Review AND repair the store-location / tax-jurisdiction change set  [DONE]
- Ask: "检查一下我最新的改动。我们添加了一部分预先设置的收税方式。看看还有没有哪里有问题"
- Follow-up ask: "可以，根据你的建议去形象修改。" / "去进行修改" — implement the recommended fixes.
- Scope: review only — the change set was authored outside this session (uncommitted working tree:
  `Models/TaxJurisdiction.cs`, `Services/TaxJurisdictions.cs`,
  `Settings/System/Defaults/tax-jurisdictions.json` new; `Shops.LocationCode` and
  `Orders.PricesIncludeTax` columns; inclusive-pricing branch in `Order.CalculateSectionPayment`;
  location picker in `ShopSetupWindow`; 10 new keys × 5 languages; README v3.0.0).
- Plan:
  - [x] Read every changed/new file and the diff
  - [x] Build (0 warnings / 0 errors)
  - [x] CJK sweep over `.cs`/`.xaml`
  - [x] Language-table parity + translation spot-check across all five files
  - [x] Probe the live database for what the one-shot backfill actually wrote
  - [x] Run the scratchpad harness suite
  - [x] Report findings, ranked
- Notes: build 0/0; CJK sweep found 1 (`TaxJurisdiction.cs` doc comment); language tables 555 keys ×
  5, parity exact, translations idiomatic; suite **919 passed / 2 failed** (balancecheck).
  Findings, worst first:
  1. **An inclusive location's `standardRatePercent` is never read.** `ApplyLocationMode` reseeds
     only `if (reseedMatrix && !inclusive)`, so CN 6 / JP 10 / FR 20 / ES 21 are dead data and the
     embedded tax is backed out at whatever the per-method matrix holds — which the same branch
     HIDES, leaving no way to set it. Live proof: shop 1 is `LocationCode = JP` carrying
     `RatePercent: 13` on all four methods, so its consumption tax computes at 13%, not 10% — and at
     0% for any method a shop marked tax free, though VAT does not depend on how a sale is settled.
  2. **The per-portion tax lines contradict the total in inclusive mode.** `DepositStageTax`,
     `UpdateTaxBreakdownLines` and `OrderPaymentSummaryConverter` all derive tax as
     `Received − Deposit` / `FinalCharge − FinalBase`, which is 0 when tax is embedded, while
     `TotalTax` is not. The printed receipt says "Tax 0 | Tax 0" beside a non-zero "Tax paid".
  3. **The 2 balancecheck failures are that divergence, caught.** The harness recomputes its
     expectations through the 6-arg `CalculateSectionPayment`, whose new `pricesIncludeTax` defaults
     to false, so it silently kept exclusive math while the window went inclusive (fixture shop is
     the JP one). The optional parameter is the landmine: a caller that forgets it is wrong and still
     compiles.
  4. `TaxJurisdictions.Default` reads `_defaultCode` **before** `Find` triggers `Load()`, so a file
     whose `defaultLocationCode` differs from the `CA-ON` const resolves differently on the first
     call than on every later one. Masked only because the shipped file agrees with the const.
  5. A new shop never gets `CreateForStandardRate` — the initial fill is guarded, so the seeding the
     README promises happens only if the user re-picks a location by hand.
  6. Rates are hard-coded into the display names in all five language files ("Ontario (HST 13%)"),
     which defeats the JSON being editable without a rebuild — the file's own comment says display
     names live in the language files.
  7. Reseeding on a hand-pick silently discards a configured matrix, no confirmation; picking `US`
     (rate 0) wipes tax entirely.
  8. `PopulateLocation` builds `ComboBoxItem`s into `Items` — the pattern already removed once for
     logging 4 binding errors per picker; `ItemsSource` + `DisplayMemberPath` is the house form.
  9. Doc/code mismatches: `TaxJurisdiction`'s remark and the README both claim the ORDER EDITOR hides
     a tax panel (only Shop Settings hides its matrix), and `DefaultCurrency` is documented as used
     "to seed" when only the backfill reads it. One CJK phrase in `TaxJurisdiction.cs`.
  10. No harness covers any of it: pricing mode, presets loader, corrupt-file fallback, or backfill.
- Fixes applied (all ten):
  - `Models/Order.cs` — `pricesIncludeTax` is now REQUIRED; the two modes split into
    `IncludedTaxPayment` / `AddedTaxPayment` + an `EmbeddedTax` helper (exclusive arithmetic byte-for-byte
    unchanged). The inclusive branch no longer consults `PaymentTaxRules`. `SectionPayment` gained
    `DepositTax` / `FinalTax` / `PricesIncludeTax` as init properties (kept off the positional
    constructor so it stays at seven params) and a computed `DepositStageTotal`.
  - `Services/TaxJurisdictions.cs` — new `IncludedTaxRatePercent(shop)`; `Default` forces `All` before
    reading `_defaultCode`; the JSON DTO's `PricesIncludeTax` renamed `Inclusive` with
    `[property: JsonPropertyName]` (S3218 — it shadowed the enclosing method).
  - `Models/TaxJurisdiction.cs` — `DisplayName` formats the rate from the FILE into the language
    file's `{0}`; CJK removed; the remarks that claimed the ORDER EDITOR hides a panel and that
    `DefaultCurrency` seeds a shop corrected.
  - `Views/OrderEditWindow.xaml.cs` — `IncludedTaxRatePercent` property; `ApplyStageTaxRates` takes one
    jurisdiction rate for both portions in inclusive mode and never zeroes it per method; the three
    refreshers and `UpdateTaxBreakdownLines` read `money.DepositTax` / `money.FinalTax` /
    `money.DepositStageTotal`; `DepositStageTax` helper deleted.
  - `Views/ShopSetupWindow.xaml(.cs)` — `LocationRow` + `ItemsSource`/`DisplayMemberPath`; a NEW shop is
    seeded (`reseedMatrix: _existing is null`); reseed no longer skipped for inclusive locations;
    `WouldDiscardConfiguredRules` + `ConfirmReseed` ask before discarding a customised matrix;
    `TaxInclusivePanel` states the rate; `TaxSectionHint` hides with the matrix it describes.
    The predicate is split from the prompt because a `MessageBox` inside `SelectionChanged` blocked
    the harness that drives the picker — diagnosed by enumerating the process's windows and finding a
    `#32770` dialog; see `context.md`.
  - `Converters/TaxLabelConverter.cs` (NEW) + `OrderPaymentSummaryConverter` + `MainWindow.xaml(.cs)` —
    tax labels follow the mode (`Order.Fields.IncludedTax`), on the receipt totals block and all three
    detail-panel section rows.
  - Language files ×5 — rates replaced by `{0}`; new `Shop.Tax.InclusiveRate`,
    `Shop.Tax.ReseedConfirm`, `Shop.Tax.ReseedConfirmTitle`, `Order.Fields.IncludedTax` (559 keys each).
  - `tax-jurisdictions.json` comment and the README v3.0.0 entry corrected to what the code does.
  - Harnesses: `scratchpad/taxcheck` NEW (253 assertions — presets, both modes, names in every
    language, upgrade path, settings screen, the discard predicate); `balancecheck` now derives the
    mode and rate from the shop instead of assuming a 13% card surcharge.
  - `SKILL.md` gained §4a (second pricing mode) and had its own labels-by-key rule applied — six
    Chinese UI labels in §4/§14 replaced with keys, one of them (实收定金) stale since the v2.0.1
    rewording. Logged in `SkillUpdates.md`.
- Notes: final state — build 0 warnings / 0 errors, Sonar clean (the one issue the IDE raised,
  S3218, fixed), suite **1174 passed / 0 failed across 21 harnesses**, 559 keys × 5 languages with
  exact parity, 0 CJK in `.cs`/`.xaml`.
  Live DB backed up to `orders.db.bak-preTaxLocation` before running the guards. Tree uncommitted.

### 2026-07-29 18:05 — Hotfix: a fully-deposited order locks its own balance controls  [DONE]
- Ask: "Price chaging bug: If my charge on the pretax is 123, the current tax rate is 13%, i
  paied pretax deposit 123, then I marked it as charged. >Then the final balance
  automatically ticked. however the global tick is ticked as well (this is good) required:
  the global tick/untick on checkbox(结清所有尾款) should automatically untick itself and untick
  for all services. even though the final balance is 0. User is able to pick the fianal
  balance payment type, even though it is 0. Logic seems awkward, because we have paid the
  full deposit, the reason is that we want to keep pricing logic for 结清所有尾款 globally the
  same. Another feature：add 应收定金 to the breakdown pricing， once checkbox on the pre-tax
  deposit is ticked, then display 实收定金 at the price breakdown as well. >Apply the same logic
  for fianal balance section. >Update wording. 实收定金 实收尾款 改为已收定金，已收尾款。"
- Three things, one of them a real defect:
  - [ ] The "clear all final balances" master must untick, and take every section with it,
        even when a section's final balance is zero
  - [ ] The final-balance payment method stays pickable at zero
  - [ ] Breakdown gains the DUE amounts (应收定金 / 应收尾款) beside the received ones, with the
        received line appearing once its portion is marked received
  - [x] Wording: 实收定金 → 已收定金, 实收尾款 → 已收尾款
- **TWO independent things put the tick back**, which is why it looked unfixable from the UI:
  - `AutoCompleteSection` re-evaluated "deposit covers the total AND is received" on EVERY
    refresh, so it re-ticked the section the next time anything recomputed. Now it fires
    only on ENTRY into that state (`if (!wasAutoCompleted)`), which is what the existing
    `wasAutoCompleted` flag was always trying to express. A convenience must not be a rule
    the user keeps losing an argument with.
  - The master's own state came from `IsOrderBalanceCleared()` — "is anything owed" — which
    is TRUE for a fully-deposited section whatever the tick says. New
    `AreAllSectionsMarkedCleared()` reads the section TICKS instead. The two questions
    diverge exactly in the reported case, and only there.
- **The money model was left alone.** `Order.IsSectionCleared`'s `FinalBase <= 0` rule is a
  fact about money (nothing IS owed) and drives `FinalBalance`, the receipt and the list
  column. Only the checkbox's own state changed. The status display and the picked-up gate
  still use the money question, deliberately — an order with a full deposit showing
  "Outstanding" against a $0 balance would be worse than the oddity of a cleared status with
  the box unticked.
- Requirement 2 (final payment type pickable at a zero balance) fell out of requirement 1:
  `IsSettled` gates on the tick, so once the untick sticks the radios unlock. Asserted rather
  than assumed.
- Breakdown gained a DUE line per portion (the TAXED figure — what the customer actually
  hands over) with a RECEIVED partner that appears only once that portion is confirmed.
  Label and value hide together: a lone label reads as a value that failed to load, and a
  zero would be indistinguishable from a portion that was genuinely free.
- Notes: build 0 warnings / 0 errors. Suite **910 passed / 0 failed across 20 harnesses**;
  new `scratchpad/balancecheck` is 24 of them and reproduces the report's exact numbers
  (123 pre-tax, 13% card, 123 deposit).
- **Two harness traps, both the same shape as ones already recorded:**
  - The snap-back arrived on the NEXT recompute, not on the click. An assertion taken
    straight after unticking passed while the bug was fully present — the harness has to
    force a refresh and re-assert.
  - A new order opens with the alteration category on "None", which switches the service off,
    so the price box is ignored and every figure reads zero. Six assertions failed for a
    reason that had nothing to do with the fix.
  - Expected amounts are COMPUTED from `CalculateSectionPayment` and formatted in the
    currency the window is actually using, never written as literals: the fixture shop prices
    in a zero-decimal currency, so a hard-coded "25.99" failed against a correct "¥26".
- **Added to the same hotfix: "served by" on the receipt.** Ask: "add to hotfix change / Add
  last modified by crew member's name just for receipt recording purpose."
  - `Order.LastModifiedBy` stores the **rendered name**, not the login and not a foreign key.
    A receipt is a historical document: resolving the name at print time would change what an
    old receipt says the day somebody is renamed and blank it the day they are deleted — and
    accounts live in credentials.json, outside this database, so there is nothing to key on.
    `UserAccount.DisplayLabel` is the value (name if they have one, else the login, never blank).
  - Stamped in `ApplyEditableFields` beside `LastModifiedDate`, from the SESSION rather than
    from anything on the form: "who saved this" is not a field anybody should be able to type.
    Left untouched when nobody is signed in, so a save can never blank a real name.
  - Printed via `AddReceiptInfoLineIfHasValue`, so an order from before the column simply omits
    the line — a label with nothing beside it reads as a printing fault, not as an absence.
  - Label `Order.Fields.LastModifiedBy` in all five languages ("Served by" / Order.Fields.LastModifiedBy / Servi par /
    Atendido por / 担当者).
  - Follow-up in the same turn ("app的订单详情里也要显示出来"): shown in the **order detail
    panel** too, directly under `LastModifiedDate` — the two answer one question together.
    Label and value share a `StackPanel` so they hide as a PAIR on an order that predates the
    column; hiding only the value would leave a heading with nothing under it, which reads as
    a control that failed to load rather than as an absence. Same rule the receipt follows.
- **A model column broke four harnesses, in two different ways** — both the same underlying rule
  and worth separating:
  - Three read the LIVE database, which had not been started since the column was added, so the
    guard had never run. Fixed by running the shipping guards against it (new
    `scratchpad/livemigrate`, reflection rather than a copied ALTER) — which is exactly what the
    user's next launch does.
  - `headercheck` migrates its own shared fixture and called `EnsureShopSchemaAsync` **alone**,
    so it covered Shops and not Orders. It had already been fixed once for this exact symptom
    when a Shops column was added; the first ORDERS column added afterwards broke it again the
    same way. It now runs both guards, in the order startup runs them.
- Notes after the addition: build 0/0, suite **921 passed / 0 failed across 20 harnesses**
  (balancecheck 35). Live database backed up to `orders.db.bak-preServedBy` before migrating.
- **Harness note worth keeping:** the "is this row hidden" assertions read `TextBlock.IsVisible`,
  not `Visibility`. A child of a Collapsed panel still reports `Visibility.Visible` for itself,
  so testing the element alone would report a hidden row as shown — which is the entire claim.

### 2026-07-29 16:10 — Currencies derived from installed languages + a localization panel  [DONE]
- Ask: "As our application grows, each store may support global clients. So accepted
  lanuages should be very dynamic, at the same time, currencies may be vary. so, as we
  already set CAD, USD, and CNY. >this nees to change, if en-US or en-CA lanuage avaialbe
  (any of them is ok), always keep CAD and USD on top. the other lanuages, even though we
  have simplified Chinese, we will use Chinese currency. For countries that have their own
  currencies, just show their own currencies. >Aslo, to make the Shop setting of UI
  cleaner, create a separate panel for adjust languages and currencies. Action: click the
  link with icon to adjust store lanuage and currencies, show another panel >Left hand show
  selected lanauge, right showing all supported currencies for each language. >Supported
  currencies should be dynamically decided by all system installed lanauges from setting.
  such as en-US.xml zh-CN.xml...etc. if installed as zh-CN, show CNY by default. en-US is
  exception, you can display CAD and USD at the same time. show CAD above USD. >Require to
  use the main themed Morden UI and design the whole structure."
- Plan:
  - [ ] Each language file DECLARES its own currencies, in order — `Currency.Codes`. That
        keeps "adding a language is dropping a file in" true for currencies too, and turns
        the en-US CAD+USD exception into data rather than a branch in code
  - [ ] `CurrencyType` grows EUR + JPY so the shipped languages can name what they imply
  - [ ] `ShopCurrencies.Offered(localization)` — the set derived from INSTALLED languages,
        English's leading
  - [ ] New `ShopLocalizationWindow`: languages left, the currencies each contributes right
  - [ ] `ShopSetupWindow` loses the two inline blocks, gains a link + icon
  - [x] Keys in all five languages; harness
- **The mapping is DATA, in the language file.** Each `*.lang.xml` declares `Currency.Codes`
  — en-US `CAD,USD`, zh-CN `CNY`, fr-FR/es-ES `EUR`, ja-JP `JPY`. So "adding a language is
  dropping a file in" now covers its currency too, and the en-US CAD+USD exception the ask
  called out is a value in a file rather than a branch in code. A build shipping en-CA
  instead needs no change here. Order is load-bearing and asserted: English's currencies
  lead, CAD before USD.
- `CurrencyType` grew EUR and JPY. The enum still BOUNDS what can be stored — the integers
  are a compatibility surface on two tables — so a declared code the enum cannot name is
  dropped rather than guessed at. That is the honest limit of "fully dynamic".
- **JPY forced a real fix, not a cosmetic one.** Yen has no minor unit, so `¥1,695.00` is
  wrong in the same way the wrong symbol was. `CurrencySettingService.Format` now owns
  symbol AND decimal places, and the four sites that were hand-building `{symbol}{x:N2}`
  go through it.
- New `ShopLocalizationWindow`: languages left, the currencies each brings right, opened
  from a link card in Shop Settings. **A currency appearing under two languages (EUR, under
  Français and Español) is ONE row object shared between the cards** — two independent tick
  boxes for one currency would let the panel contradict itself, with no answer to "does
  this shop take euros".
- Six string keys were orphaned by the move and removed from all five files. The harness
  found them; the skill's "prune orphaned keys" rule is the reason it looks.
- Notes: build 0 warnings / 0 errors. Suite **886 passed / 0 failed across 19 harnesses**
  (currencycheck 80, up 29; formatcheck 108; langcheck 88 — its shop-editor section now
  drives the panel).
- **Three defects the harness and a screenshot caught, in that order:**
  - Adding EUR/JPY to the enum without adding their string-table entries put
    **"CurrencyType.EUR" on screen** — an unresolved key renders as the key, which reads as a
    broken control rather than a missing translation. Found by LOOKING at the render. There
    is now an assertion that every storable currency has a name, so looking is not the
    mechanism next time.
  - `Shop.Localization.Summary` is `{0}  ·  {1}` — pure punctuation, identical in every
    language, so the fallback sweep flagged it. Same category as the separators; allow-listed.
  - Hand-built `ComboBoxItem`s added to `Items` before the ComboBox is in a visual tree log
    four binding errors each (the stock template's `RelativeSource FindAncestor` alignment
    bindings have no ItemsControl to find). `langcheck` counts binding errors as failures,
    which is what surfaced it. Fixed by using `ItemsSource` + `DisplayMemberPath`, which also
    makes the picker a view over the same rows the right pane ticks.

### 2026-07-29 14:40 — A shop supports 1..N currencies; an order records its own  [DONE]
- Ask: "Add another feature, based on the store supported lanauge, we can choose supported
  currencies as well."
- Modelled on the store-languages feature, but currency is NOT display — a number in CNY is
  not the same number in USD — so the money model has to change with it. User chose
  **per-order currency** (recommended) over a shop-level display toggle, and no new enum
  members.
- **Two defects found while surveying, both latent today and both fatal the moment a shop
  has a second currency:**
  - Every money display reads `CurrencySettingService.Instance.Symbol` — the SHOP's current
    currency — never the order's. Switching a shop's currency would silently re-denominate
    every historical order: a ￥1,695 order printing as "$1,695.00". The Symbols table's own
    comment already calls that out as "not a cosmetic bug, it is the wrong number".
  - `Order.CurrencyType` exists as a column and `OrderEditWindow` **never writes it**, so
    every order ever saved carries the default `CAD` — including all 44 in the CNY shop. The
    column is both unread and wrong.
- Plan:
  - [ ] `Shop.SupportedCurrenciesJson` + accessor + setter, mirroring the languages pair
  - [ ] `Services/ShopCurrencies` — the one answer to "which currencies may this shop use",
        with the same never-empty fallback that made the language change invisible
  - [ ] Column guard in `ShopColumnMigrations`, and CREATE TABLE kept in step
  - [ ] **Backfill `Orders.CurrencyType` from each order's shop** on the same one-shot hook —
        without it, this change makes every existing CNY order render as dollars
  - [ ] Shop editor: tick list + preferred picker containing only what is ticked
  - [ ] Order editor: currency picker over the shop's set, hidden at one, writes the order
  - [ ] Every display site reads the ORDER's currency: list converter, receipt, editor totals
  - [ ] Shop picker card and user management show the supported set
  - [x] String keys in all five languages; seed data; harness
- **The plumbing was already there and disconnected.** Every `CurrencyAmountConverter`
  MultiBinding in `MainWindow.xaml` ALREADY passed `CurrencyType` as `values[1]` — the
  converter read `CurrencySettingService.Instance.Symbol` and threw the second value away.
  So the list and the whole detail panel were a one-line fix. Someone built this for
  per-order currency and then wired it to the global setting.
- `Services/ShopCurrencies` mirrors `ShopLanguages` deliberately, and differs twice, both
  recorded at the top of the file: **no per-user capability** (an administrator sees every
  LANGUAGE because language is only how a screen reads; currency is a fact about the order,
  so pricing outside the shop's set would put a wrong number on a real receipt), and the
  answer is bounded by an **enum** rather than a discovered folder.
- **Order.CurrencyType is stored as a NAME in the shop's set, not the enum's integer.** The
  numbers are an implementation detail; reordering the enum would otherwise silently
  re-denominate every shop.
- The editor keeps an order's own currency in the picker even when the shop has stopped
  accepting it. Dropping it would show a different currency beside unchanged amounts and
  saving would re-denominate a finished order — what a shop takes today does not reach back
  and restate what it charged.
- Notes: build 0 warnings / 0 errors. Suite **855 passed / 0 failed across 19 harnesses**;
  new `scratchpad/currencycheck` is 51 of them. The decisive check changes the SHOP's
  currency under an existing order and asserts the order does not move.
  Backfill verified on a rewound copy of the live database: **44 orders in the CNY shop
  started wrongly marked CAD, and 191/191 matched their shop afterwards.**
- **fitcheck broke and the break was worth more than the feature.** It read
  `SystemParameters.WorkArea`, passed on the 1280×752 laptop it was written on, and failed
  the moment the machine moved to a 2057×1323 display — every window fitted, nothing scaled,
  nothing left to prove. `WindowFitting.Fit` gained a `(Window, Rect)` overload so the screen
  is an INPUT rather than ambient state, and the harness now simulates a 1366×768 laptop on
  any machine. Two further faults surfaced while fixing it: the first `Fit` was a silent
  no-op because layout had not run (`root.ActualHeight` was 0, so five geometry assertions
  failed while printing nothing — `UpdateLayout()`, not a dispatcher pump), and the
  proportions check NRE'd instead of failing, reporting a crash where the honest answer was
  a failed assertion.

### 2026-07-29 13:30 — Fit every window to the screen it opens on  [DONE]
- Ask: "UI improvements: The application open in smaller screen, sections of UI (e.g.
  eidt order panel-> cancel and save record button section) is cut. >TODO, to make sure
  the application can be operated in different screen, can we make the UI proportionally
  scale down based on the screen size?"
- **Diagnosed before designing.** This machine's work area is **1280×752**.
  `OrderEditWindow` declares `MinHeight="900"`, so the window can never be shorter than
  900 DIPs: the bottom 148px — which is exactly the pinned Cancel/Save footer — is off
  screen and **unreachable**, because the minimum forbids resizing it into view. The
  layout itself is already right (Auto title / `*` ScrollViewer / Auto footer); nothing
  is mis-laid-out. The window simply asserts a minimum bigger than the display.
- Survey of every window against a 1366×768 laptop (work area ~728):
  - **Cannot fit at all** (MinHeight > work area, footer unreachable): `OrderEditWindow`
    900, `CustomMadeServiceWindow` 900.
  - **Opens taller than the screen** but can be resized down: `DocumentPreviewWindow`
    1040, `ReceiptBrandingWindow` 880, `ShopSetupWindow` 840, `MeasurementTermsWindow`
    760, `StoreMembersWindow` 760, `MainWindow` 1440 (maximized, so benign in practice).
- Plan:
  - [ ] One helper, registered once — a `Window.Loaded`/`SourceInitialized` CLASS handler
        so every window is covered and a NEW window cannot be forgotten
  - [ ] Scale from the MINIMUM, not the design size: `MinHeight` is the author's
        statement of "below this the layout breaks", and the ScrollViewer already handles
        content beyond it
  - [ ] Never scale UP (a 4K screen must not get giant chrome), and floor the scale so
        text cannot become unreadable — below the floor the ScrollViewer takes over
  - [ ] Use the work area of the monitor the window actually opens on, not the primary
  - [x] Harness: assert the footer is inside the work area at small screen sizes
- **`Controls/WindowFitting`**, registered from `App.StartApplicationAsync` as a
  `Window.Loaded` CLASS handler. One registration covers every window including any added
  later — the per-window alternative fails by omission, which is exactly how this shipped.
  `LayoutTransform` (never `RenderTransform`: only a layout transform makes the content
  MEASURE smaller, which is what lets the minimum come down — a render transform looks
  identical in a screenshot while the window goes on demanding 900px). Reads the work area
  of the monitor the window is actually on via `MonitorFromWindow`, converted device→DIP.
  Never scales up; floors at 0.5 as a backstop.
- Measured result on this machine (1280×752): editor scale **0.820**, window 1027×752,
  minimum 863×752 — down from a minimum of 900 that could not fit — and the Save button's
  bottom edge at y=725 against a work area ending at 752. It was previously unreachable.
- **Two harness bugs found before the code was trusted, both worth keeping:**
  - `PointToScreen` returns **device** pixels while every WPF size is device-independent.
    On this 150% display a correctly-placed button reported y=1087 against a 752 work area
    and read as catastrophically broken; 1087 device px is 725 DIP. The dangerous
    direction is the opposite one — on a 100% monitor the raw comparison passes, so a
    genuinely broken layout would have looked fine.
  - `TransformToAncestor(window)` followed by `window.PointToScreen(...)` double-counts the
    ancestor transform. Ask the ELEMENT; it walks the chain once.
- **Checked rather than assumed:** a `Popup` DOES inherit an ancestor `LayoutTransform` for
  rendering — a ComboBox drop-down under a 0.820 window draws its items at 0.821. Worth
  measuring because the opposite holds for BINDINGS across a popup boundary
  (`CalendarSizing`), so the separate-visual-tree rule does not carry over.
- Windows that already fit are provably untouched (scale exactly 1, declared size and
  minimum intact), and a hidden/re-shown window is not scaled twice — `Loaded` fires again
  and a second transform would compound.
- Notes: build 0 warnings / 0 errors. Suite **802 passed / 0 failed across 18 harnesses**;
  new `scratchpad/fitcheck` is 22 of them, and every assertion is measured geometry on a
  real window rather than a reading of the XAML. It opens with a precondition that this
  screen really is smaller than the editor was drawn for, without which the whole file
  would pass vacuously on a large monitor.
  **Gate caveat, stated rather than glossed:** neither the SonarLint nor the IDE-diagnostics
  tool is available in this session, so Gate 1 and Gate 2 could not be RUN. The new file was
  reviewed by hand against the rules in SKILL.md §10 (all members static; native methods
  wrapped per S4200; no nested ternary; complexity well under 15; every `MonitorInfo` field
  documented as required by the native layout even though only `Work` is read). Worth a
  Sonar pass when the tooling is next available.
- Ask: "now remove all chinese words comments from all places" → narrowed to
  "Just keep English comments across the application" → "you need to add this into SKILL
  as well, even though I communicate with you by Chinese, you still need to use English
  to develop everything"
- **62 comments across 25 files.** The rule was already in `SKILL.md` and had been broken
  steadily anyway, which is the interesting part: not one of these was careless. Each was
  a comment naming a menu or a field by the Chinese label the developer had just been
  looking at — perfectly clear while writing, unreadable to half the people who will
  maintain the file. It only becomes visible in aggregate, which is why it needed a sweep
  rather than review.
- Rewritten, not deleted. A comment saying "drives the Custom Service list flag" carries real
  information; the fix is to name the thing by its **key** (`Order.Fields.CustomMadeFlag`),
  which is what the skill asked for all along and is strictly better than either label —
  keys are greppable and survive a re-wording. Navigation paths took the English labels
  instead (`Local Configuration → Switch Shop`), which read better than a pair of keys.
- Three comments needed real rewriting rather than substitution, because the Chinese WAS
  the explanation: the name-splitting rule (why a Chinese name goes whole into the first
  name), the per-language address example, and the `LanguageOption.ToString()` sample.
  Each now makes the same point in English without the literal.
- **Method.** A reviewed table of (file, line-range, replacement) rather than find/replace
  — the comments are English prose with labels embedded, so blind substitution would have
  produced sentences nobody proofread. The applier refuses any range that does not
  actually contain CJK, so a stale line number overwrites nothing: 62 applied, 0 skipped.
  The replacement text lives in a **UTF-8 JSON** file read with an explicit encoding,
  because Windows PowerShell 5.1 decodes a BOM-less `.ps1` as ANSI and would have mangled
  every em dash in the new text.
- **Left alone, deliberately:** the `zh-CN` and `ja-JP` string tables (that IS the data),
  the language names in `README.md` (a language naming itself), and punctuation quoted to
  describe it (`（）` against `( )`). One Chinese menu path in `README.md` was fixed — same
  defect as the code comments. The skill-folder docs still hold Chinese, almost all of it
  verbatim `- Ask:` quotes, which are a record of what was said rather than prose.
- `SKILL.md` "Who this skill is" rewritten with the reason it erodes and a **grep to run
  before finishing** — a rule with no check is a preference. See `SkillUpdates.md`.
- Notes: comment-only diff, 25 files, 79 insertions / 78 deletions. Build 0 warnings /
  0 errors; formatcheck 106/0 (its source guards grep the tree), langcheck 88/0,
  uicheck 30/0.

### 2026-07-29 11:20 — Japanese (ja-JP) as a fifth system language  [DONE]
- Ask: "To now lest do another adding on the lanuage, lets say Japanese"
- Run under the scope fixed in `SKILL.md` §1a the turn before: key parity, translation
  precision, all-languages-deleted — **not** a full application re-test. Only `formatcheck`
  and `langcheck` were run.
- **Cost: the file, its data, and nothing else.** 529/529 parity first time; the discovery
  count, the per-language fallback sweep and the placeholder sweep all picked ja-JP up with
  **zero harness edits**, which is the return on generalising them during the Spanish round.
  Six values are identical to English (three currency codes, `Measure.Unit.Cm`,
  `Format.BulletSeparator`, `Order.Fields.ServiceTotalLineNoDetail`) and every one was
  already exempt — **no new cognates**, unsurprising for a language sharing no script with
  English.
- **Why Japanese was worth doing rather than a second Latin language.** It is the second
  CJK language, so it is the first real test that punctuation is DATA: `、` with no
  trailing space, fullwidth `（）`, corner brackets `「」`. Three shapes no other shipped
  language uses. The generic "differs from English" sweep would NOT have caught a wrong
  one — `", "` and `"、"` differ either way — so `formatcheck.RunJapanese` asserts each
  shape explicitly, including that one key (`Format.ListSeparator`) now punctuates three
  different ways across zh / ja / en.
- **Found while seeding, and NOT part of this work:** printing each shop's name in every
  shipped language rather than only the new one showed **Vancouver had no French name**,
  so a French reader had been seeing 温哥华工作室 since fr-FR shipped. Invisible because
  the fallback renders something. Backfilled, and the habit is now in `SKILL.md` §1a.
- `langseed`'s per-shop names went from a `SpanishName` field to a `(code, name)[]` list —
  the second language would have added a second field, then a third. Same reason the
  language files are discovered rather than listed.
- Mock data: #1 LeeYonge **zh+en+ja** (Japanese in a partial set), #2 Tianbao **all five**
  (still the "installs everything" fixture langcheck looks for — this entry has to grow
  with every addition or it silently stops being that), #3 Vancouver es+en opening in es,
  #4 Montréal fr+en+es, #5 Toronto en only. Backup `orders.db.bak-preJapanese`.
- `langcheck`'s Spanish window check was generalised to every language the app did not ship
  with, deriving the word it looks for from the language's own table instead of a literal
  per language. It now renders es / fr / ja and sweeps each for raw string-table keys.
- Notes: build 0 warnings / 0 errors. formatcheck **106 passed / 0 failed** (↑9),
  langcheck **88 / 0** (↑8). Screenshot `main-window-ja-JP.png` — the five-language picker
  strip still fits: `简体中文、English、Español、Français、日本語`.

### 2026-07-29 09:10 — Spanish (es-ES) as a fourth system language  [DONE]
- Ask: "Now, lets add a new lanuage spanaish into it. lets see if the Lanuage setting
  is running well"
- Treated as a TEST of the claim that "adding a language is dropping a file in". The
  deliverable is the language; the finding is whether anything outside
  `Settings/System/Languages` had to change.
- Plan:
  - [x] `Settings/System/Languages/es-ES.lang.xml` — all 529 keys, key-parity clean
  - [x] `Format.ListSeparator` / `Format.BulletSeparator` correct for Spanish
  - [x] Verify every language list is discovered (toggle, shop editor, print, PDF)
  - [x] Install it on a shop and exercise the toggle / print scope
  - [x] Harness suite green; extend `langcheck` / `formatcheck` where they assume three
- **The claim held for CODE and broke for DATA.** No `.cs`, no `.xaml`, no `.csproj`
  edit — the csproj already globs `Settings\**\*`, `LocalizationService` discovers the
  folder, every language list is built from `AvailableLanguages`, and
  `ShortLanguageName` derives the export suffix from the BCP-47 primary subtag
  (`es-ES` → `Measurements_es.pdf`) rather than listing known languages. What DID need
  work was the data around it, and none of it is visible from the code:
  - `Shop.InstalledLanguagesJson` stores an EXPLICIT list, so the shop that installed
    "all three" silently became a shop installing three of four. "All" was never a
    value, only a snapshot.
  - Every existing shop was **nameless in Spanish**, and `Shop.ResolveName` falls back
    to `values.Values.FirstOrDefault(…)` — dictionary insertion order, not English.
    Vancouver's first stored name is Chinese, so a Spanish reader saw 温哥华工作室.
    Documented behaviour ("any other language that has one"), invisible until a
    language is added. Fixed with data, not by re-ordering the fallback, which would
    change what every other language falls back to.
- **Cognates.** `Branding.Color` → "Color" and `Order.Fields.Subtotal` → "Subtotal"
  are the Spanish words. `formatcheck`'s "no key silently falls back to English" sweep
  flags them, so a `(key, language)` exemption was added rather than the
  shared-across-all-languages list — that list exempts a key in EVERY language and
  would have stopped anyone noticing the same key left untranslated in French. Padding
  the Spanish out ("Color del texto") to satisfy a test would have put worse Spanish on
  screen, which is the wrong trade.
- **Punctuation as data, exercised again.** es-ES quotes with «» where en-US uses curly
  quotes, so `Delete.ConfirmMessage` reads `¿Eliminar el pedido «ORD-1»?`. Asserted,
  because it is exactly the class of thing that gets concatenated in code by mistake.
- Mock data (`scratchpad/langseed`, backs up to `orders.db.bak-preSpanish` — a NEW
  name, since `.bak-preLanguages` is the only copy of the pre-installed-set state):
  #1 LeeYonge zh+en · #2 Tianbao **all four** · #3 Vancouver **es+en, opening in es-ES**
  · #4 Montréal fr+en+es · #5 Toronto en only, left alone. That covers 1/2/3/4 installed
  languages, a shop that OPENS in a language added after the app shipped, and Spanish as
  an ordinary member of a set rather than only as part of "everything". The seeder now
  states each shop's preferred language rather than inferring "keep it if still
  installed", which would have left every shop where it was and skipped that case.
- **Harness coupling paid off and removed.** `formatcheck` asserted
  `AvailableLanguages.Count == 3` and `== 4` in two places, so the fourth language
  failed tests that had nothing to say about it. Counts now come from the folder, and
  the two per-language sweeps (nothing falls back to English; placeholders agree)
  iterate `AvailableLanguages` rather than `new[] { Fr, Zh }`. Language five costs no
  edit here. `langcheck`'s "widest card" reasoning said three languages was the longest
  the picker strip gets; it now finds the widest by counting shipped languages named.
- Notes: build 0 warnings / 0 errors. No repository `.cs` changed, so there was nothing
  for either quality gate to analyse beyond the build. Suite **745 passed / 0 failed
  across 17 harnesses** (formatcheck 79 ↑54 — it absorbed the generalised sweeps;
  langcheck 80 ↑5). New coverage: the whole MainWindow rendered in Spanish with the
  visual tree swept for text that is still a raw string-table key — the shape a
  half-wired language actually takes on screen, where nothing throws and one panel in
  five reads `Toolbar.NewOrder`. Screenshots: `main-window-es-ES.png`,
  `shop-picker-languages-es-ES.png`.
- README: the v1.0.0 record left intact as history; a "Since v1.0.0" note records the
  addition. No version bumped and no tag cut — not asked for.
- **Follow-up, same turn — the removal side, which had NO coverage.** User set the scope:
  "just need to test if keys are added identical, plus the translation is percise. no
  need to rerun and retest the whole application. but needs to test if all lanauges are
  deleted." Recorded as a convention in `SKILL.md` §1a (and `SkillUpdates.md`); the tests
  went into `formatcheck` (79 → **95**, and only formatcheck was re-run, per the scope).
  - **A real asymmetry, found by testing it rather than reasoning about it.** An empty
    folder is refused by the file-count guard which runs BEFORE `Load`, so the loaded
    table survives and the UI keeps rendering. Files that PARSE but declare no
    `code`/`name` get into `Load`, which calls `_translations.Clear()` before it can know
    the load will fail — so that path leaves the service empty and every key renders as
    itself. Harmless today because startup aborts either way; it is precisely what an
    in-app "reload languages" would have to work around, so both are now pinned.
  - The empty/missing messages name the folder, which is what makes the failure findable
    from a screenshot. Asserted, since a message that merely says "no languages" would
    pass a naive check and help nobody.
  - Startup's own guard asserted from source (`OnStartup` try/catch → unlocalized
    MessageBox → `Shutdown(1)`), because driving real startup would migrate the schema
    and start the host. The failure being prevented is the one App's own comment
    describes: an exception past the first `await` in `async void` vanishes the process
    with no window and no message.
  - Removing ONE language: the picker drops it, `SetLanguage` refuses it and leaves the
    current language alone, a shop installing it keeps the rest and opens in one that
    still ships, and a shop whose ONLY language was removed still resolves to something
    rather than opening unrenderable.

### 2026-07-28 23:15 — "Sign in as this user" for an administrator  [DONE]
- Ask: "Add new feature. >If you are login as admin, when manage user panel, you can
  choose to login as the user. Place a button for that. add svg icon."
- `AuthenticationService.SignInAs(userName)` hands the session to another account with
  no password. It grants an administrator nothing new — they can already set anybody's
  password and reach the same session in two more clicks — what it buys is SEEING the
  application as somebody else: which shops they get, which chrome is hidden, what
  their language toggle offers.
- **Gated in the SERVICE, not only in the UI**, unlike the roster edits beside it.
  Those write data; this hands out a session, so the check belongs where a new call
  site cannot skip it. Refuses: a non-administrator, yourself, an unknown account, and
  one every shop has delisted — that last would spend the administrator's own session
  to land on "no shop is available" and then the login screen.
- **The bound shop is cleared** on the switch. Capabilities would otherwise go on
  resolving against the shop the ADMINISTRATOR had open, which the new user may hold no
  role in. The picker binds one again immediately after.
- Two routes, because User Management opens from two places:
  - `MainWindow` → `App.SignInAsAsync`, structurally a sign-out that skips the login
    window: main window down first (a capability swap under a live window leaves the
    previous person's chrome on screen), then the shop picker again.
  - `ShopPickerWindow` → reported up as `SignInAsUserName` and the picker closes;
    `OpenInitialShopAsync` performs the switch and returns the new `ShopSelection.UserSwitched`,
    which makes the existing loop round again as the new user. A THIRD state, not a
    flavour of Cancelled — folding it in would sign the new user straight back out.
- The window only REPORTS the choice; switching from inside a dialog would pull its own
  ground out from under it, and the caller owns the main window that has to come down.
- Button sits in the identity card, with the person it acts on, rather than in the
  footer where it would read as another thing Save does. Hidden — not disabled — when
  it does not apply, per the convention on every other gated control here.
- **On "add svg icon":** drawn as WPF `Path` geometry, which IS SVG path syntax. An
  `.svg` file cannot be rendered at runtime — no rasterizer is installed on this machine
  (context.md), which is why `app-icon.svg` exists only as a design source for the `.ico`
  — and a bitmap would not stay crisp at every DPI. Same technique as the Store Members glyph
  in `MainWindow`.
- Notes: build 0/0, Sonar 0, suite **731 passed / 0 failed across 17 harnesses**
  (`namecheck` 102, up 15). New: the button's availability (offered / your own account /
  delisted) and every service rule, including that administrator rights and the bound
  shop are both gone after the switch. The click itself raises a confirmation modal,
  which cannot be answered in-process, so the harness drives everything up to it.

### 2026-07-28 22:30 — Authentication: taken-name feedback, and the login that would not save  [DONE]
- Ask: "Improve authentication. IF the user name is registered, you need to let user
  know that the username is not available when add new user name. >I check the admin
  role, that trying to update user name, the system blocked me to update user name.
  what is the reason? Do you have any concerns? If you cannot do, you can just gray
  that area." — then, mid-turn: "管理员的登录名还是不能修改。这个rule不变" /
  "其他人的登录名可以改，问题是现在改不了，save的时候会回退" /
  "可不可以不用alert，直接用red error message" / "提示username 被占用了" /
  "算了 你还是加回来吧".
- **The real defect: TWO buttons labelled Save Changes.** The profile card carried one
  (`OnSaveContactClick`) and the footer carried the primary one (`OnSaveClick`) — and
  the footer's saved only the password and the shop roles. Anyone who edited a name or
  a login and pressed the obvious button watched the edit vanish on the reload that
  followed, under a "changes were saved" message. That is the "save 的时候会回退".
  Now ONE Save for the screen: profile → password → roles, profile first because it
  may rename and everything after has to act on the new login.
- **A bug I introduced last turn and have now removed.** `ApplyRename` was renaming the
  `ProvisionedAccounts` entry. That list records which SEED NAMES have been created, and
  `ProvisionSeedAccounts` looks each seed name up in it — so renaming `staff` to `sam`
  left `staff` unlisted and the next load seeded a fresh `staff` **with a known
  password** beside the renamed one. The old name staying put is exactly what prevents
  that. My comment last turn asserted the opposite; namecheck agreed with it because it
  only ever renamed an account that was never seeded.
- Seeding now identifies the administrator by its **flag**, not its name. Behaviour is
  unchanged, but the "exactly one administrator" invariant no longer rests solely on
  the rename guard.
- Administrator's login stays locked, per the user. The box is **disabled** (greyed)
  rather than read-only — a read-only box looks editable and silently swallows typing,
  which is what "the system blocked me" with no explanation was.
- Taken names are reported **as they are typed**, on the create form, the roster's add
  form and the login box. `IsUserNameTaken` / `IsUserNameTakenByAnother` on the service;
  the save path still re-checks, so this is the courtesy, not the guard. Availability is
  settled BEFORE the rename confirmation — asking "rename to X?" and only then saying X
  is unavailable wastes the question.
- Login errors moved UNDER THE LOGIN (`LoginErrorText`) instead of the shared line at
  the foot of the card, which was showing the same message twice; and a failed save
  now clears the stale "changes were saved".
- The rename confirmation was removed and then restored on request. It is a native
  MessageBox, so `namecheck` cannot answer it — a XAML window cannot be subclassed
  (`InitializeComponent` resolves its resource by exact type) and the alternatives are
  test hooks in shipping code. The harness therefore drives the button for everything
  except a confirmed rename and covers the rename against the service; the boundary is
  written at the call site so the gap is visible rather than assumed away.
- Notes: build 0/0, Sonar 0, suite **716 passed / 0 failed across 17 harnesses**
  (`namecheck` 87, up 11). New coverage: renaming a SEEDED login and reloading, the
  administrator refusal, live availability on both forms, and the footer Save persisting
  the profile card — the last of which is the check whose absence let this ship.

### 2026-07-28 21:40 — Orders list: one line per cell, uniform row height  [DONE]
- Ask: "UI improve: For all the main records section, Do not wrap the words for the
  each column. if the screen cannot hold as much content(overflowed) you can have a
  scroll bar to the right. each record keeps the same height. >if the text is over the
  column width defined. use \"...\""
- One wrapping `TextBlock` was doing all the damage: the Custom Service column stacked the
  garment names under the flag with `TextWrapping="Wrap"`, so a row listing several
  garments was TALLER than its neighbours. That is the one thing a list read by
  scanning down a column cannot afford, and it is invisible in source — the list looks
  fine until somebody's order has enough garments.
- Three columns (CustomerName, Status, BalanceStatus) used `DisplayMemberBinding`,
  which generates a bare `TextBlock` that cannot be styled: an over-long value was
  CLIPPED mid-glyph, with no ellipsis and no way to read the rest. Converted to
  `CellTemplate`s.
- New `ListCellText` in the theme — `VerticalAlignment=Center`, `TextWrapping=NoWrap`,
  `TextTrimming=CharacterEllipsis` — and `NumericCellText` now derives from it, so
  every cell in the list gets the behaviour from one place. NO size and no colour, for
  the reason already recorded on `NumericCellText`: the row takes its size from the
  font-size slider and its colour from the gray-out trigger.
- The garment column is now a **Grid**, not a horizontal StackPanel: a StackPanel gives
  its children infinite width, so the names would never know they had overflowed and
  `TextTrimming` would never fire. The star column is what makes the ellipsis work.
- Full values moved to `ToolTip` on the columns that can overflow — an ellipsis that
  hides data with no way to see it is a worse trade than a clipped glyph.
- `HorizontalScrollBarVisibility` Disabled → **Auto**. With nothing wrapping, a window
  too narrow for the columns can only be answered by scrolling to them; Disabled left
  the rightmost columns unreachable.
- Notes: `MainWindow.xaml` + `Themes/AppTheme.xaml` only. No code-behind, no new keys.
  Build 0/0, Sonar 0, suite **691 passed / 0 failed across 17 harnesses**.
  New `scratchpad/rowcheck` (20) measures REAL `ListViewItem`s in a real window at both
  ends of the font-size slider (12px and 40px, since wrapping bites hardest when the
  text is large): every row exactly one height (54 / 66.2), no cell wrapping, every cell
  trimming — plus a guard that at least one cell really IS too long for its column, or
  the ellipsis checks would pass on a list where nothing was ever truncated. It seeds
  that long value rather than hoping the user's data contains one.

### 2026-07-28 20:55 — First/last name, account labels, editable login  [DONE]
- Ask: "User experience improvement. >User management Panel should include person's
  Full name. The display of Accounts should be like `Tina（Manager,Staff)`, In the main
  section, it should display the user's login as well. Admin has the right to update
  user's login too. >Split user's real name to be first and last name. >In the main
  application, the welcome message should display Hi {Firstname}, you are log....."
- Plan:
  - [x] `CredentialRecord.DisplayName` → `FirstName` + `LastName`; credentials.json
        schema 3 → 4 with a lossless split
  - [x] `UserAccount` / `StoreMember` / `MemberProfile` carry both; `FullName` derived
  - [x] User management: list label `Name (Roles)`, detail shows + EDITS the login
  - [x] Rename guarded (never the administrator) and renames the `ProvisionedAccounts`
        entry too, or the old login gets re-seeded
  - [x] Store members: first/last boxes on the edit and add forms
  - [x] Greeting uses the FIRST name
  - [x] String keys in all three languages; `scratchpad/namecheck`
- **The name split, and why the rule is conservative.** No whitespace — "林艳",
  "Prince" — puts the WHOLE value in `FirstName` and leaves the last empty. A Chinese
  name is family-name-first with no separator, so a positional guess would greet 林艳
  as "林", addressing her by her surname alone; keeping it whole is right for that case
  and merely incomplete for a mononym, which is the better failure. With whitespace,
  split at the LAST space: "Mary Jane Watson" → "Mary Jane" + "Watson". Lossless either
  way — re-joining gives the original back.
- `PersonName` (Full / Label / Greeting) is the one composer. `Label` never returns
  blank (falls back to the login); `Greeting` is the first name, which is the ask.
  Recorded limitation: the join is given-name-first with a space, i.e. the western
  order. Making it a language rule the way `Format.ListSeparator` is would be the fix
  if it ever matters — it only affects somebody who fills in BOTH boxes.
- **The dangerous half of renaming a login is `ProvisionedAccounts`.** That list is
  what stops a seeded account being created again on the next load. Rename `staff` to
  `tina` without updating it and the next launch sees `staff` as never provisioned and
  seeds a brand-new one — with a known password — beside the renamed original. Also
  fixed while there: `RefreshCurrentUser` identifies the session BY USER NAME, so after
  a rename the record no longer matches itself and the session silently kept a login
  that no longer existed. The caller now decides before renaming and adopts after.
- The administrator's login is refused outright: it is a `const` this file tops up on
  every load, so renaming it would leave the installation with TWO administrators. The
  box is read-only rather than hidden, and says why.
- Rename and the rest of the card are ONE call (`UpdateAccountProfile`), validated
  before anything is written — a rename that landed while a bad phone number was
  rejected would leave the pane describing an account that no longer answers to it. It
  asks for confirmation first: the consequence lands on somebody else, at their next
  sign-in, with nothing on their screen to explain it.
- `Users.AccountLabel` is `{0}（{1}）` in zh and `{0} ({1})` in en/fr — the whole shape,
  because Chinese brackets fullwidth. An account holding no role is just its name;
  empty brackets read as a rendering fault.
- Notes: build 0/0, Sonar 0 (one S2365 cleared by making `HeldRoles` a method — a
  property that allocates invites being read in a loop). Suite **671 passed / 0 failed
  across 16 harnesses**, `namecheck` 62 of them.
  Three harnesses broke on the signature change and were repaired, not worked around:
  `authcheck`'s `MemberProfile` helper, `uicheck`'s `DisplayNameBox` lookup, and
  `formatcheck` — `Users.AccountLabel` is identical in en and fr, which is punctuation
  rather than a missed translation, so it joins the shared-value allow-list beside
  `Format.ListSeparator`.
  Confirmed against the user's real file, which had already migrated by the time it was
  inspected: manager → Jimmy Wong, staff → Yong Wu, staff2 → Tinna (first only), all
  correct.

### 2026-07-28 20:20 — Shop picker cards name the languages each branch runs in  [DONE]
- Ask: "Improve the user experience, in Select Shop after login, The store item
  should also show supported lanuages within `shops you can open` section."
- The card's metadata strip already had a language slot — it showed the shop's
  PREFERRED language, so a bilingual branch advertised exactly one. It now shows the
  INSTALLED set, which is strictly more informative and is the set a manager or staff
  member will actually be able to switch between once inside.
- Replaced rather than added beside it. Two language facts on one card is noise, and
  for a single-language shop they are the same string printed twice.
- **Plain text, not a row of chips.** Languages are DISCOVERED, so an installation can
  ship any number; a strip that ellipsizes in a star-width column degrades predictably,
  where a growing stack of badges would change every card's height. The role badge on
  the right also already owns the "chip" idiom on this card.
- Two joins on one line, which is the distinction the two APIs exist for:
  `JoinList` punctuates the languages as prose (`简体中文、English` in zh,
  `简体中文, English` in en/fr), `JoinFragments` separates the strip's fields with ` · `.
- Rendered in all three languages rather than reasoned about, because French runs ~25%
  longer and a three-language shop is the widest this strip ever gets:
  `人民币 · 简体中文、English、Français · 订单 44 笔` /
  `CNY · 简体中文, English, Français · 44 commandes`. Both fit with room to spare.
- Notes: `Views/ShopPickerWindow.xaml.cs` only — `BuildDetails`. No XAML change, no new
  string keys, no schema change. Build 0/0, Sonar 0 findings, suite
  **609 passed / 0 failed across 15 harnesses** (`langcheck` 75, up 7).
  The new checks read the REAL rows the window builds rather than re-deriving the
  string: the bug being fixed was a card that named one language for a two-language
  shop, and only reading what the row actually says can tell those apart. One check
  sweeps every shop in the live database for a card naming no language at all.

### 2026-07-28 19:40 — An English-only shop with 40 orders  [DONE]
- Ask: "Update local DB, and add a new store with 40 orders, assign only english
  lanuage to it."
- Plan:
  - [x] `scratchpad/englishshop` — new shop, `InstalledLanguages = ["en-US"]`
  - [x] 40 orders through the application's own model / money / numbering
  - [x] Verify against the live database and re-run the suite
- Shop #5 **Toronto Bespoke**, code TOR, CAD, installs `en-US` and nothing else, so
  its staff and managers get NO language toggle while an administrator standing in it
  still sees all three. 40 orders: 13 with custom-made measurements, 19 with
  ready-made lines, 63,102.76 CAD total.
- Its NAME and ADDRESS are still per language (zh/en/fr). What a shop RUNS IN and
  what it is CALLED are different questions — an administrator working in Chinese
  should read a Chinese name for an English-only branch.
- Numbering is **YearlySequential** (`TOR-2026-0001`), the fourth mode, so Timestamp /
  Sequential / DailySequential / Yearly are now all represented across the
  installation.
- **Worth knowing:** seeded orders are back-dated up to 240 days, which crosses a year
  boundary, so the yearly counter legitimately runs two series and RESTARTS between
  them — 36 numbers in 2026 and 4 in 2025. No duplicates: `Reserve` scans for numbers
  already taken, which is exactly the guard that makes a thrashing counter safe. The
  seeder now asserts the distinct count and prints what the next real order would take
  (`TOR-2026-0037`, free) rather than printing first/last by id, which had made 40
  orders look like 36.
- Idempotent (a second run adds 0) and backs the database up to
  `orders.db.bak-preEnglishShop` once, not on every run.
- Notes: suite **602 passed / 0 failed across 15 harnesses**.
  `authcheck` had to be repaired first — see context.md. It asserted on the seeded
  `test1`/`test2` accounts, which had been deleted in the application; that deletion is
  permanent by design (`ProvisionedAccounts`), so five checks were failing over a
  legitimate user action. It now CREATES the fixture accounts if they are missing and
  still restores the file byte-for-byte, so the user's deletion stands.

### 2026-07-28 18:20 — Store-scoped languages: install 1..N per shop  [DONE]
- Ask: "TODO features: Improve Store lanuages.\n\n>Change roles lanuage
  visibility, staff user now can view multiple lanuages. if the store is binding
  with lanuages. \n\nFor example, if Store 1 is installed with Chinese and English,
  then any user can view at least two lanuages with toggle. \n\n>Store needs to pick
  minimum of 1 but support as many as the system supports. \n\n>Do a moocking data
  update on the existing DB, and assign lanuages accepted for them for testing
  purpose.\n\n-Be aware of the printing functionality for lanuages. \n\n>Manage and
  staff user can view installed lanuage(s), but Admin can view all languages., show
  a simple message under login status message, say that The installed languages is:
  xx or are: xx,xx\n\n>If the store support only 1 lanauge, do not show language
  toggle.\n\nUse WPF-dev role to run this, harness QA is required."
- Plan:
  - [x] `Shop.InstalledLanguagesJson` + `ShopColumnMigrations` guard
  - [x] `Services/ShopLanguages` — the one answer to "which languages may this
        session pick from", installed set vs. administrator's all
  - [x] `MainWindow`: scoped toggle, hidden at one language; installed-languages
        line under the greeting
  - [x] `ShopSetupWindow`: multi-select install list, preferred picked from it
  - [x] Print + PDF language pickers use the same scope
  - [x] String-table keys in all three languages
  - [x] Mock data on the live database
  - [x] `scratchpad/langcheck` harness
- **The rule, in one place.** `Services/ShopLanguages` answers three questions and
  nothing else reimplements any of them: `Installed(shop)` — the languages a branch
  runs in; `Selectable(shop, canChooseAnyLanguage)` — what THIS user may pick, which
  is every shipped language for an administrator and the installed set for everyone
  else; `PreferredCode(shop)` — the language the shop opens in. It sits outside both
  `AuthenticationService` and `ShopContext` because the answer is a product of both,
  and it is consumed by four surfaces (toolbar toggle, shop editor, measurement print
  dialog, PDF download panel) — the number at which a copied rule starts drifting.
- **`Installed` is never empty, and the fallback is what keeps the change
  invisible.** A shop with nothing installed reads back as just its
  `PreferredLanguageCode`, which reproduces the previous behaviour exactly: one
  language, no toggle. A shop that has said nothing at all — no installed set AND no
  preference — has restricted nothing, so it gets everything. Both are the shop's own
  statement read as literally as possible. Codes whose `*.lang.xml` no longer ships
  are dropped, and the set comes back in SHIPPED order rather than stored order.
- `AuthenticationService.CanChooseLanguage` → **`CanChooseAnyLanguage`**. Under the
  old name `false` read as "no language toggle", which stopped being true the moment
  a shop could install more than one. Renaming forced both call sites to be re-read.
- **`App.ApplyActiveShop` keeps the language on screen when the shop installs it.**
  Previously only an administrator's choice survived opening a shop; now a staff
  member who picked English at login keeps it in a shop that runs in English, and is
  moved only when the shop does not install their language. The move goes through
  `ShopLanguages.PreferredCode`, not `shop.PreferredLanguageCode` — the two can
  disagree, and opening a branch in a language its own toggle cannot return to is
  worse than either.
- Editor: `Shop.Setup.InstalledLanguages` is a tick box per shipped language, and the
  preferred-language picker lists **only what is ticked** — enforcing "opens in a
  language it installs" by what the control CONTAINS rather than by validating it
  afterwards. Save refuses an empty set (`Shop.Setup.InstalledLanguagesRequired`); a
  new shop starts with the administrator's current language and nothing else, since
  installing a language a branch's staff cannot read is not a neutral default.
- Print paths share the scope and collapse their language row at one option — the
  radio is still created, so an export is never languageless.
- Keys added to all three files: `Language.Installed.One` / `.Many` (separate keys:
  English and French both inflect "language is" / "languages are"),
  `Shop.Setup.InstalledLanguages` + `Hint` + `Required`.
  `Shop.Setup.PreferredLanguageHint` reworded — it said non-administrators are always
  stuck in this language, which is no longer true.
- Mock data (`scratchpad/langseed`, backs the database up first, idempotent): #1
  LeeYonge zh+en, #2 Tianbao all three, #3 Vancouver en only (the hidden-toggle case),
  #4 Montréal fr+en. Runs the shipping `EnsureShopSchemaAsync` by reflection rather
  than re-typing the ALTER, so the seeded column is the one the app adds.
- **Fixed alongside, same block:** the greeting went stale on a language switch —
  it is written from code and `OnLanguageChangedGlobally` never re-ran it. The new
  installed-languages line sits directly under it and would have done the same.
- **Fixed alongside:** the orders search box had no `x:Name`. `pagingcheck` reached it
  as "the first TextBox in the window", which silently became a ComboBox's internal
  `PART_EditableTextBox` as soon as the language picker stopped being collapsed for a
  harness with nobody signed in. Named `SearchBox`, with an `AutomationProperties.Name`.
- Notes: build 0/0, Sonar 0 findings on every changed file (one pre-existing S8969 in
  `MeasurementSheetDocument` cleared while the analyzer was installed). Suite
  **599 passed / 0 failed across 15 harnesses**; `langcheck` is 68 of them.
  Two harness repairs were needed to get there and neither was caused by this work —
  see the entries in context.md: `headercheck` had an undeclared ordering dependency
  on migcheck migrating their shared fixture, and `uicheck`'s menu check had been
  failing silently since the menus were themed. All 14 harnesses that pointed at
  `scratchpad/navswap/bin` were repointed at the project's own `bin/Debug`, which
  removes the stale-assembly trap for good.

### 2026-07-28 15:45 — Cancelling the shop picker signs out instead of exiting  [DONE]
- Ask: "After login and in Select store panel, the Cancel button should
  automatically logout. do not close the application."
- Cancelling the picker called `Shutdown()` — on BOTH paths, startup and sign-out.
  That is a trapdoor: sign-in and shop selection read as one flow, so Cancel on the
  second step is taken to mean "go back", and instead the application vanished with
  no way to hand the machine to a colleague short of launching it again.
- New `App.OpenShopOrSignInAgainAsync` loops: open a shop; if the picker is
  cancelled, sign out and show sign-in again. Both call sites use it, so the two
  paths cannot drift. `Shutdown()` is now reached only when the LOGIN window is
  dismissed — the one gesture that still unambiguously means "I am done".
- Signing out is the point rather than a side effect: the session is authenticated
  by the time the picker appears, so returning to sign-in while still signed in
  would leave the previous user's session live behind the login window.
- Falls out of the same change: an account assigned to no shop used to be told so
  and then have the application close under it. It now returns to sign-in, so
  somebody else can take the machine.
- Notes: new `scratchpad/logoutcheck` (9). It drives App's real private loop by
  reflection and answers each dialog from a `Window.Loaded` class handler, so the
  assertions are about what the loop DOES with a cancelled picker rather than about
  a re-implementation of it. Verified: picker → login (not shutdown), signed out by
  the time sign-in appears, dismissing sign-in still reports cancelled, and
  picker → login → picker opens a shop and rebinds the session.
  Build 0/0, suite 483/0.

### 2026-07-28 15:05 — Accessibility: arrow keys page the order list  [DONE]
- Ask: "Improve accessibility: while in the main application, press right and left
  arrow, you can jump into the next page of records."
- Paging existed but was reachable only by clicking two small buttons under the
  list. Left/Right now page from anywhere in the window.
- **`PreviewKeyDown` on the Window, NOT a `KeyBinding`.** An InputBinding fires
  whatever has focus, which would page the list every time somebody moved the caret
  in the search box — the shortcut would have made the app less usable, not more.
  Handling the tunnelling event lets `ConsumesHorizontalArrows` stand down for
  `TextBoxBase` / `PasswordBox` / `ComboBox` / `DatePicker` / `Slider` / `MenuBase`.
  It walks UP the tree because focus lands on a part inside the control (a
  ComboBox's editable TextBox, a DatePicker's inner box), so testing the focused
  element alone would miss it and steal the key anyway.
- Any modifier stands down: Alt+Left is "back" almost everywhere and Ctrl+Left is
  word-wise caret movement; neither should be quietly redefined.
- Accessibility beyond the shortcut: the page summary is an
  `AutomationProperties.LiveSetting="Polite"` live region with an explicit
  `LiveRegionChanged` raised on each change (rebinding text alone does not raise
  it), so a screen reader announces where you landed. After paging, selection and
  focus move to the first row of the new page — otherwise a keyboard user is left
  on a page whose rows they cannot reach until they Tab back in, and a screen reader
  has nothing to read. Both pager buttons gained a tooltip and
  `AutomationProperties.HelpText` naming the shortcut, so it is discoverable.
- Notes: new `scratchpad/pagingcheck` (14). Build 0/0, suite 474/0.
- **Harness lesson, learned the expensive way:** `InputManager.ProcessInput` with a
  fabricated `KeyEventArgs` is NOT usable for this. It needs the keyboard device
  bound to a real foreground window, so events are silently discarded — the first
  assertion failed while later ones passed, and on another run every key vanished.
  Worse, the Alt/Ctrl/Shift assertions PASSED because their input was being thrown
  away: a green light for the absence of behaviour. Replaced with
  `target.RaiseEvent(PreviewKeyDownEvent)`, which is deterministic (14/14 on three
  consecutive runs) and still exercises the real tunnelling route. The modifier
  assertions were **deleted rather than kept**, because `Keyboard.Modifiers`
  reflects the physical device and cannot be faked in-process.

### 2026-07-28 14:10 — Contact number and email on every login account  [DONE]
- Ask: "给所有login user添加contact number 和email"
- `PhoneNumber` / `Email` added to `CredentialRecord`, and through it to `MemberProfile`,
  `StoreMember` and `UserAccount`. **Account-level, not per membership**: someone
  who works at two branches has one phone and one mailbox, and per-membership
  storage would let the two disagree. Both nullable, so an existing
  `credentials.json` is already valid — no schema bump, no migration.
- Editable in TWO places on purpose. Store Members covers people who belong to a shop;
  `CreateAccount` deliberately makes accounts that belong to none (`test1`,
  `test2` show "No shops assigned"), and the roster cannot reach those at all. So
  User Management gained a matching card backed by a new `UpdateAccountContact`,
  which touches no membership and is therefore safe on the administrator and on
  one's own account — unlike a role change, filling in a phone number grants
  nothing.
- Validation extracted to `Models/ContactValidation` and shared with the order
  form rather than copied. A member address the roster accepts but the order form
  rejects is a defect nobody sees until mail bounces. Blank is valid — both fields
  are optional — and persists as `null`, never `""`, so "no phone number" has one
  spelling rather than two that print differently.
- Labels reuse the existing `Members.*` namespace (`Members.Phone` /
  `Members.Email`, added to all three files); the Save button reuses `Users.Save`.
- Notes: `authcheck` +11 assertions covering round-trip, trimming, null-on-blank,
  the account-level path, that a role change does not wipe contact details, and
  the shared validator's accept/reject sets. `uicheck` repaired — it had been dead
  since the config refactor (loading the removed root `Languages.xml`) and its
  credentials backup pointed at the pre-`UserDataPaths` location, so it was
  silently backing up nothing; it now renders both changed screens, 0 binding
  errors. Suite 460/0; build 0/0.
- **Found, not caused:** `admin`'s password is no longer `admin` in the live data
  folder, exactly as `staff`'s was earlier. The record is structurally intact
  (salt, hash, iterations, `IsAdministrator: true`). `authcheck` now pins `admin`
  too — `SetPassword` is gated by its callers, not by the service, so it can be
  called without signing in first, which is what makes pinning admin possible.

### 2026-07-28 13:30 — Download-measurement language picker made dynamic  [DONE]
- Ask: "In download measurement section, it should detect a third language as well.
  right now only Chinese and English. Make this section dynamic."
- Two literal radios (`DownloadChineseRadio` / `DownloadEnglishRadio`) and
  `DownloadEnglishRadio.IsChecked ? "en-US" : "zh-CN"`. French shipped as a full
  system language that measurements could not be exported in, and a fourth would
  have been invisible the same way. The PRINT dialog
  (`MeasurementPrintOptionsWindow`) was already dynamic, so print offered three
  languages while download offered two.
- Now an `ItemsControl` filled from `LocalizationService.AvailableLanguages`, the
  same list the print dialog and the login screen use. Each radio is labelled with
  the language's OWN name from its own file, so a new language names itself rather
  than needing a translated entry added to every existing file — which also made
  `Download.Language.Chinese` / `.English` dead, and they were removed.
- Default follows the UI language, with a fallback so a selection always exists.
  `ShortLanguageName` already derived the file suffix generically.
- Notes: `gendercheck` +19, opening the real window under each of zh-CN/en-US/fr-FR
  and asserting one radio per installed language, each labelled with its own name,
  exactly one selected, the UI language default, and — the part that matters — that
  the export method actually reads the picked code. It also asserts three or more
  languages are installed FIRST, without which the whole check passes vacuously on
  a two-language install and proves nothing.

### 2026-07-28 12:50 — Measurements sheet: the receipt's generated letterhead  [DONE]
- Ask: the downloaded PDF opened "GST/HST 税号：… / 量体打印单 / 订单编号: …" — the tax
  number above the title, and the shop never named. "For downloaded PDF and printed
  PDF for measurements, they should be aligned in the same structure... follow the
  same logic like printing header&footer for receipt. This is a global setting."
- Both measurement paths injected the registration number at the TOP of the page
  and built no letterhead at all. The receipt had grown one (`AddReceiptTitle` →
  shop name → subtitle → contact lines → tax number LAST) and the two measurement
  paths never followed; the old code comment even said so.
- New `Services/ShopLetterhead` holds it as plain resolved strings — name,
  subtitle, contact lines, tax line — built for an explicitly passed language,
  since the sheet is generated in the language chosen in the print dialog. All
  three consumers now build from it: receipt, printed sheet, PDF export.
- Rules, taken from the receipt: the tax number is the LAST letterhead line; a
  custom header REPLACES the generated letterhead rather than stacking on it (a
  shop that typed its address into the editor must not get the shop record's
  address printed underneath as well); and the document title is the letterhead's
  subtitle, moving into the BODY when a custom header replaces the letterhead — in
  both formats, so print and download stay structurally identical either way.
- Notes: `pdfcheck` +5 — the decisive one renders with and without the tax line and
  asserts the band above it is byte-identical, which only holds if it is last.
  `headercheck` +6 reflects into the real `BuildMeasurementDocument` and asserts
  the printed block order matches. Suite 449/0.
- **Correction to the previous entry's claim:** I reported "all harnesses now
  reference a single artifact" after changing only `pdfcheck`. `authcheck`,
  `seeder` and `uicheck` point at the project's own `bin`, which the scratch
  `OutputPath` builds never update — so those ran against stale code. Build to the
  normal output path whenever the app is not running.

### 2026-07-28 12:20 — Measurements PDF: keep the header/footer, improve the layout  [DONE]
- Ask: "打印量身尺寸的PDF 文件应该要保留header 和footer， 优化一下PDF的UI。"
- Root cause of the missing letterhead: the logo, the branded header, the tax line
  and the branded footer were all composed into `page.Content()`. QuestPDF renders
  Content once, flowing across pages; only `page.Header()` / `page.Footer()`
  repeat. So a sheet that fitted on one page looked right, and a sheet that ran to
  two carried branding on page one alone with the footer stranded wherever the
  last garment ended. Both now sit in the page's own slots.
- Second defect, found only by rendering a two-page sheet: a garment's name sat at
  the foot of page one with its four measurements orphaned, unlabelled, at the top
  of page two. Wrapping heading + table in one `column.Item().Column(...)` does
  **not** make them atomic — a Column splits like anything else. The garment name
  is now the table's `Header` row, so QuestPDF repeats it on every page the table
  spans.
- Extracted `Services/MeasurementSheetDocument.cs` (+ `MeasurementSheetContent`,
  `MeasurementSheetSection`, `MeasurementSheetRow`). `CustomMadeServiceWindow` now
  only gathers already-localized data and calls `Save`. The window cannot be
  opened without a message loop, so while the layout lived there it could only be
  checked by a human clicking Export.
- Layout changes: page numbers ("1 / 2") centred under the footer; order/customer
  details grouped into a bordered tinted card; garment headings in the app accent
  `#4F46E5` with a left accent bar; measurement rows striped. The colon moved from
  the value (`": 9051234567"`, which reads as a missing field name) to the label.
  Info labels get a 132pt column, garment terms keep 190pt — term names run ~25%
  longer in French and a wrapped label costs more than a wide gap.
- `ResolveTaxRegistrationNumber` moved to `ReceiptBrandingStore`; `MainWindow`
  delegates. The receipt and the PDF both print it and both had their own copy.
- Fixture bug in my own harness, worth recording because it fails **silently**:
  `BrandingRenderer` does `XamlReader.Parse(xaml) as FlowDocument`, so branding
  with a `Section` root casts to null and renders nothing. Needs `[STAThread]` too.
- Two harness assertions were wrong before the code was: bands guessed as
  "the top 10%" ran into the content area, and pixel comparison across a taller
  header tests the anti-aliaser rather than the layout. Both replaced — bands are
  located from the rules the layout draws, and the title-fallback check compares
  body *height*.
- Notes: new `scratchpad/pdfcheck` (26 assertions) renders the real document to
  images and asserts the letterhead is byte-identical on all 4 pages, the footer
  likewise, the page number differs between pages, and no continuation page opens
  with orphaned measurements. Repaired `authcheck`, which had rotted twice over
  (asserted an already-migrated file against a shop list that had since grown; and
  signed in with a `staff` password that had been changed in the app) — it now
  rewinds its own fixture and pins its passwords, 52/52 and idempotent.
  Suite 419/0 across 11 harnesses; build 0 warnings / 0 errors; Sonar clean.

### 2026-07-28 11:10 — Bug: printing measurements in inches printed nothing  [DONE]
- Ask: "打印量身尺寸转换成inch功能似乎有问题，没有打印的内容。"
- Root cause, measured rather than guessed: `CustomMadeMeasurementReader` did
  `var display = isInch ? value.In : value.Cm;` and `continue`d when it was blank. A
  `MeasurementValue` only carries BOTH units if the editor's cm/inch toggle happened to be
  flipped while that value was on screen; anything typed in cm and saved has no `In` at all.
  A probe over the live database: **768 measurement values, 768 with a cm figure, 39 with an
  inch one** — so 729 of 768 rows were skipped, and an order that was never toggled produced
  `rows.Count == 0` → a null section → a sheet with nothing on it.
- Done:
  - [x] `Models/MeasurementUnits` — `Convert` (cm↔inch, preserving a trailing +/-) and `Resolve`
        (the requested unit, converted from the other one when it is missing).
  - [x] `CustomMadeMeasurementReader` and the QuestPDF export both resolve instead of reading the
        field directly. `CustomMadeServiceWindow.ConvertMeasurement` now delegates to the same
        helper, and its private `MeasurementNumberPattern` / `CentimetersPerInch` are gone.
  - [x] The conversion lives in ONE place on purpose: the editor, the printed sheet and the PDF
        have to produce the same figure — a printed sheet disagreeing with the screen about a
        customer's chest measurement is worse than one that shows nothing.
- Notes: build 0/0, Sonar zero, **355 assertions across 10 harnesses**.
  - `scratchpad/inchprobe` runs the REAL reader over every real order in both units:
    111 sections / 768 rows in cm, **111 / 768 in inches**, 0 orders empty. Before the fix,
    inches yielded 39 rows.
  - Conversion rules asserted too, including that "20+" converts to "7.87+" — dropping the mark
    would silently change what the measurement means — and that free text is returned unchanged.
  - One assertion of mine was wrong, not the code: I expected the 10cm round trip to give 9.99;
    it gives 10.01, because each hop rounds to 2dp. Replaced the literal with the real property
    (drift ≤ 0.1cm).

### 2026-07-28 05:55 — Receipt letterhead: one labelled line per contact detail  [DONE]
- Ask: reformat the sub-header from an unlabelled address plus a bullet-joined
  `phone · email · website` into `Address: …` / `Phone: …` / `Email: …` / `Website: …`,
  "using existing text keys if available".
- Done, and both halves of the ask were already in the codebase:
  - [x] Labels come from `Shop.Setup.Address` / `.Phone` / `.Email` / `.Website` — the SHOP's own
        field names. Deliberately not `Order.Fields.*`, which are the CUSTOMER's address and phone;
        the two sets exist separately for exactly this reason. No new keys, and the letterhead is
        translated for free (the French shop renders "Adresse:", "Téléphone:", "Courriel:",
        "Site web:").
  - [x] Rendered through the existing `ReceiptInfoLine`, so "Address: …" on the letterhead reads the
        same as "Customer name: …" in the panel below it — same muted-label treatment, one renderer.
        Overridden only for size (10.5pt) and leading (1px rather than the panel's 3px: this is a
        four-line block and at panel spacing it would rival the order details for height).
  - [x] The trailing gap is applied to the LAST line actually written, not as a fixed block margin —
        otherwise a shop that has filled nothing in leaves a hole under its name.
- Notes: build 0/0, **341 assertions across 9 harnesses**. `receiptshot` now asserts each detail is
  on its own labelled line AND that the bullet-joined form is gone, so the old format cannot return.

### 2026-07-28 05:40 — Per-shop product catalogue + receipt letterhead  [DONE]
- Ask: "Make already-made products dynamically carried with store settings" (add/modify management,
  loadable defaults) and "Improve printing" (left-align title/subhead/GST-HST/footer, shop contact
  details on the receipt, GST/HST field in shop settings, header&footer overrides global, careful
  on font size).
- **Product catalogue.** The ready-made categories were a `static readonly string[]` in
  `OrderEditWindow`, so every shop in every installation sold the same five things and a sixth meant
  a rebuild. Now `Models/ProductCatalog` + `Services/ProductCatalogService`, modelled on
  `MeasurementTermsService` — one JSON file per shop keyed on `PublicId`, seeded from the shipped
  defaults, with add / rename / remove / reorder / restore-defaults, copied by the new-shop wizard's
  "copy from an existing shop", and edited through a new `ProductCatalogWindow` (Local Configuration → Product Categories,
  gated on `CanConfigureShop`).
  - **The shipped ids are a COMPATIBILITY SURFACE, and that drove the design.** Every order ever
    saved holds one in `OrderItem.ProductName` and every language file has a matching
    `ClothingItem.<id>` entry, so the predefined entries keep their original ids and take their names
    from the string table — which also means they stay translated into languages added later. Only
    user-added categories carry their own per-language names. `ResolveName` falls back id → string
    table → the raw id, so an order naming a category the shop has since deleted still prints.
  - Opening an order whose category was removed re-adds it as a one-off entry rather than silently
    re-filing it under whatever sits at index 0.
  - Per-language naming reuses `MeasurementTermLanguageWindow` in its garment mode (no gender
    picker) rather than reimplementing it — so a language added later appears in both editors.
- **Receipt.** Title, subtitle, GST/HST and the logo default are all LEFT aligned now; the shop's
  address / phone / email / website print in the letterhead when set (address per language, the rest
  single-valued); `Shop.TaxRegistrationNumber` added (model + BOTH schema lists) with a field in
  shop settings; the header/footer editor's number OVERRIDES the shop's, being the more specific
  surface. Font sizes: 18 name / 12 body / 11 tax / 10.5 contact, asserted to stay ≥10 and ≤18.
  - **Bug found by rendering it: the tax number printed ABOVE the shop's own name.** It was inserted
    at the very top, which is right when a custom header replaces the letterhead and wrong when the
    generated one is already there. Now placed by the letterhead itself, with the top-insert kept
    for the custom-header and measurement-sheet cases.
- Notes: build 0/0, Sonar zero, **336 assertions across 9 harnesses**. 513 keys per language, parity
  exact. Backups taken before both live writes (`Backups/orders.db.bak-preTaxColumn`).
  - `scratchpad/catalogcheck` — 45 assertions: defaults, per-language resolution in all three
    languages, the legacy/unknown-id fallback, add/rename/remove/move/restore, persistence across a
    re-bind, per-shop isolation, and a guard that the five shipped ids never change. It uses
    throwaway `PublicId`s and compares the data folder before and after, so it leaves nothing behind.
  - `scratchpad/receiptshot` — renders the real receipt and asserts NO top-level paragraph is centred,
    every contact field is present, and the font sizes stay in range.
  - **Three formatcheck failures were the USER's change, not a regression**: `app-defaults.json` now
    says `en-US` (their commit "Change the default to en"), and my assertions hardcoded `zh-CN`. The
    file is untracked so git showed no diff. Fixed by asserting the file is HONOURED rather than
    that it holds a particular value — a test that pins a user-owned setting reports their
    configuration change as a defect.

### 2026-07-28 04:50 — Gender picker: alignment and symbols  [DONE]
- Ask: "性别分类UI做好看一些，标签跟语言选择一样，下拉菜单可以跟语言选择的input一样宽。同时在下拉菜单用
  男女svg符号加在前面。通用的话也用个通用的svg符号。" then, mid-turn: "你不用自己新建svg，其他地方有".
- Done:
  - [x] The gender row is now literally one of the language rows' shape — a 120px label column and
        the control in the remainder. Asserted, not eyeballed: the drop-down measures **246px, the
        same as a language name box, at the same left edge (140px)**, and the label sits in the same
        column (20px) in the same colour as 简体中文 / English / Français.
  - [x] Symbols in front of each option: ♂ / ♀ / ♂♀.
  - [x] `Views/MeasurementGenderPresentation` — ONE table for the symbol and the localized name.
        The terms-list badge now reads from it too, instead of its own private switch.
- **Course correction, and the user was right to make it.** I first hand-drew three vector Paths,
  reasoning that ⚥ (U+26A5) is not reliably present in a UI font. The user pointed out the symbols
  already exist elsewhere — the terms list has badged rows with the CHARACTERS ♂ / ♀ since long
  before this. Reusing them is better on every axis: no second definition to drift, and both marks
  are already proven to render in this application. "Common" is the two marks together, which needs
  no third glyph at all and sidesteps the U+26A5 coverage problem entirely.
- **Then I made the exact mistake I had just documented.** I gave the symbol a fixed `Width="26"`;
  "♂♀" needs 38.4px, so its second glyph was cut and rendered as what looked like a missing-glyph
  box — i.e. it looked precisely like the font problem I had used to justify drawing vectors.
  Fixed with `Width="Auto"` + `SharedSizeGroup`, which measures the widest mark instead of guessing
  and keeps the three labels on a common left edge. `Grid.IsSharedSizeScope` on the ComboBox so the
  closed face is covered as well as the open list.
- Notes: build 0/0, Sonar zero, **282 assertions across 7 harnesses**, 0 binding errors.
  - `gendercheck` grew the assertion that would have caught the clipping without a screenshot: the
    combined mark's rendered width against its measured width (38.4 vs 38.4). It also renders the
    Common state to a second screenshot — the justification for using characters was that they are
    proven to draw here, so that had to be shown, not asserted as a string.

### 2026-07-28 04:30 — Gender picker: radios → drop-down  [DONE]
- Ask: "在量身项目设置中，新添加全身量身项目后，多语言名称的性别分类改成下拉菜单把，radiobutton对其他
  西方字母语言不友好。"
- The user was right, and the harness put a number on it. Three radios in a row need the width of
  ALL THREE labels at once, in a 420px dialog:
  | language | width three radios needed | fits 420px? |
  |---|---|---|
  | zh-CN (通用（男女均适用）/仅男装/仅女装) | ~291px | yes |
  | en-US (Common (both genders)/…) | **~429px** | **no** |
  | fr-FR (Mixte (les deux genres)/…) | **~463px** | **no** |
  A drop-down needs only the widest label, and only while open. Closed control is 366px in all three.
- Done:
  - [x] `MeasurementTermLanguageWindow`: three `RadioButton`s → one themed `ComboBox`, items built
        from the string table when the dialog opens (modal and short-lived, so it never has to
        survive a language switch on screen — same reasoning as ShopSetupWindow's currency picker).
  - [x] Label moved ABOVE the control rather than beside it: a long label plus a control on one line
        reintroduces exactly the width problem being fixed.
  - [x] **Found the same fault one control over**: the Save button was a fixed `Width="90"` and
        rendered French "Enregistrer" as "Enregistre". Changed to `MinWidth` + padding, so the pair
        still look matched in Chinese but either can grow.
- Notes: build 0/0, Sonar zero, **255 assertions across 7 harnesses**, 0 binding errors.
  - `scratchpad/gendercheck` — 34 assertions, no database or credentials (the dialog needs only the
    string table). The assertions are GEOMETRIC on purpose: the point was never "a ComboBox exists"
    but "the control no longer overflows in a language with long labels". It also asserts no
    RadioButton survives, the value round-trips, the picker stays hidden in garment mode, and
    **every button in the dialog is wide enough for its own label in every language** — the check
    that would have caught the Save button without anyone looking at a screenshot.

### 2026-07-28 04:00 — Sonar to zero, French as a third language, unused-key cleanup  [DONE]
- Ask: "首先把该fix的bug要fix了，持续做下去，做到完成。做完之后添加一整套的法语作为第三种语言" +
  "add a new testing store with French language" + "with 40 records" +
  "remove Legacy Keys in language files and not in used code".
- **Sonar is at zero for the first time** — all 31 standing findings cleared, not suppressed wholesale:
  - 18 × S6602/S6603/S6605 → the collection-specific `Find` / `TrueForAll` / `Exists`. One was wrong
    (`values` was an ARRAY, needing `Array.Exists`) and the compiler caught it, which is why these
    were done as mechanical edits behind a build rather than by eye.
  - 3 × S125 "commented out code" → all three were explanatory PROSE that happened to parse as code
    (a trailing `;`, a `Directory.Move` reference). Reworded rather than suppressed.
  - 7 × S2325 "make static" → split empirically, not assumed. 5 are wired from XAML and CANNOT be
    static (the generated InitializeComponent emits `this.Handler`); those got justified
    suppressions. 2 are attached only from code and were genuinely made static — which then
    cascaded a 3rd (`RegisterDecimalTextBox`) that no longer touched the instance.
  - 1 × S3267 → a `foreach`+`if`+`break` that was a search; rewritten as `FirstOrDefault`.
- **Bug found and fixed: `\"{0}\"` in the delete dialog.** XML text has no backslash escaping, so the
  confirmation literally read `订单 \"ORD-123\"` with visible backslashes. Present in BOTH shipped
  languages since forever. Replaced with proper quotes per language (“ ” / « »).
- **French (fr-FR) added as a real third system language**, 497 keys, exercising exactly what Phase 1
  was for: a file was dropped into Settings/System/Languages and nothing else changed.
  - Two failures the harness caught that review would not have: `Paging.Summary` was word-identical
    to English (so it was silently "translated" by falling back), and the duplicate-code error
    message named the WRONG file — de-DE sorts before en-US, so the blameless original was reported.
    The message now names BOTH files.
  - Column widths: French runs ~25% longer than English. `Order Date` → `Date de la commande` and
    `Custom Service` → `Service sur mesure` truncated their headers; both columns widened, sized for
    the LONGEST language rather than whichever was open when they were set. `Payment.Status.Refunded`
    was shortened in French instead of widening a column the user had explicitly sized.
- **French test shop seeded**: #4 Atelier Montréal, fr-FR, CAD, MTL-0001…MTL-0040, 40 orders, 16 with
  a custom service. `Garments` populated from the start — the legacy flat-field shape is what made an
  earlier seeding run report "no custom service" on every order.
- **Unused keys removed**: `Order.Fields.Tax`, `Shop.Picker.RoleHere`, `Users.PasswordChanged`.
  Only 3 of 500, and the scan is the interesting part: 34 keys are never written literally anywhere
  because they are composed at runtime (`$"Measure.Term.{id}"`, `$"PaymentMethod.{method}"`, …).
  Deleting one of those is SILENT — the lookup returns the key and the screen shows
  "Measure.Term.waist". A key survives if its full name appears in source OR its prefix is one the
  code interpolates. Now a permanent guard in the harness.
- Notes: build 0/0, **Sonar zero**, **221 assertions across 6 harnesses**, 0 binding errors.
  Verified visually at 2560 (the app's default size) against the LIVE database, read-only.
  A pre-seed backup was taken first: `Backups/orders.db.bak-preFrenchShop`.

### 2026-07-28 03:05 — Config refactor, PHASE 3: the runtime data folder  [DONE]
- Ask: continuation of the phased plan ("keep going").
- Done:
  - [x] `Configuration/UserDataPaths` — ONE definition of `%LOCALAPPDATA%\CameywareOrder`. It had
        been spelled out in SIX independent places (credentials, currency, language preference,
        measurement terms, branding, database). The product has already been renamed once
        (LeeYongeOrdering → CameywareOrder); six copies is six chances to miss one next time.
  - [x] `Backups/` — new safety copies go there, and `SweepLegacyBackups` collects the ones earlier
        versions left loose at the root (23 on this machine). **The sweep never deletes**: an old
        backup is the user's, and reorganising around it is no reason to discard it.
  - [x] `Config/` — credentials / currency / language-preference migrate there LAZILY, per file, on
        first access. **On failure it returns the OLD path** so the file keeps being read where it
        is. Being unable to tidy up must never make credentials.json unreadable.
  - [x] Retention via `backupRetentionCount` in app-defaults.json (default 10, 0 = keep all).
        Deleting backups is the user's call, so the number is visible and editable rather than a
        constant in code. Applied ONLY after a new backup is written — never on startup — so nothing
        is deleted unless it has just been superseded. Ordered by write time, not by the name, since
        one real backup is called `orders.db.bak-preShopRules` and has no date to parse.
- **Deliberately NOT moved, with reasons in the code:**
  - `Documents/` — `DatabasePathProvider` writes export packages with entry paths RELATIVE TO the
    data root and extracts them the same way, so "Documents/…" is baked into every export zip a user
    already holds. The on-disk layout here is a data interchange format, not just a folder.
  - `orders.db` — named at top level in that same package, and every connection string resolves
    through it.
  - `measurement-terms-<publicId>.json` — keyed on the shop's PublicId in the FILE NAME.
  - Tidiness was not worth reopening any of the three.
- Notes: build 0/0, Sonar clean on every Phase 3 file. **212 assertions across 6 harnesses, green.**
  - `scratchpad/userdatacheck` — **40/40**, entirely against throwaway folders shaped like the real
    one. To make that possible the operations take the data root as a PARAMETER rather than each
    reaching for the real one; the alternative was a test-only seam on the class that decides where
    credentials live, and a migration that has only ever run against the machine it must not break
    is not one worth shipping. It proves: nothing deleted (5 in, 5 out), idempotent over three runs,
    live data untouched, a name collision leaves the stray rather than overwriting, retention
    removes the OLDEST by write time, and — the one that matters — **a locked file falls back to the
    original path instead of locking the user out**, then migrates once the lock is gone.
  - The last assertion proves the live folder was not touched by the harness itself.
- Live-folder state after this session: `Config/` now holds credentials.json and
  currency-setting.json (migrated lazily when `headercheck` constructed MainWindow — the app was
  closed, verified first). language-preference.json and the 23 loose backups migrate on next launch,
  since nothing in the harnesses runs `App.OnStartup`. Content hash of credentials.json verified
  identical across the move.

### 2026-07-28 02:35 — Config refactor, PHASE 2: the last unlocalized UI strings  [DONE]
- Ask: continuation of the phased plan ("可以 开始吧").
- **The phase was smaller than I had scoped it, and re-scanning is why.** I had listed 8 strings.
  Two of them — the startup-failure MessageBox and the data-folder migration message — are
  ALREADY deliberately unlocalized, each carrying a comment saying why, and both are right:
  - `App.xaml.cs` catches around the whole of startup, and loading the language table is PART of
    startup. A localized message there could depend on the very thing that failed.
  - `LocalDataFolderMigration` runs at `StartApplicationAsync` line ~87, BEFORE the table loads at
    ~93. (The load could be moved earlier — it only touches the app directory, not AppData — but
    weakening a deliberate "this runs FIRST" invariant to translate a rare error message is a bad
    trade.) Left alone; "localize everything" would have undone working design.
- Done:
  - [x] Six formatting-ribbon tooltips in `ReceiptBrandingWindow` (Bold / Italic / Underline /
        Align left / center / right) — the only literal `ToolTip=` left in the application.
  - [x] `SignOut.Failed` — the sign-out failure MessageBox showed a bare `ex.ToString()` under the
        caption "Cameyware Order". Now a plain-language line first (a stack trace alone does not
        tell the person whether they are still signed in), with the caption from `App.MainTitle`.
        Localized here BECAUSE the table is loaded by this point, unlike the two above.
  - [x] Left as-is on purpose: the `B` / `I` / `U` button faces, the alignment glyphs, `×` and `—`.
        Typographic convention, not prose — every word processor shows the same three letters in
        either language.
- Notes: build 0/0. Sonar: no new findings (the two `MainWindow.xaml.cs` S2325 are pre-existing and
  merely line-shifted). 500 keys per language file, key sets verified identical.
  **172 assertions across 5 harnesses, all green.**
  - New guards in `formatcheck`: every new key resolves in BOTH languages AND differs between them
    (identical text usually means only one file was edited), plus a source guard that **no literal
    `ToolTip=` exists in any XAML** — those six were the last, so any literal is now a new one.

### 2026-07-28 02:10 — Config refactor, PHASE 1: Settings/System + per-language files  [DONE]
- Ask: continuation of the phased plan ("可以那么按阶段来" / "继续吧").
- Done:
  - [x] `Languages.xml` (1058 lines) split into `Settings/System/Languages/{zh-CN,en-US}.lang.xml`,
        493 keys each. Split with **byte-level tooling, not XDocument** — `XDocument.Save` would have
        rewritten the `&#32;` character references back into literal spaces, undoing Phase 0's
        protection. Content verified IDENTICAL key-by-key before deleting the original.
  - [x] `Settings/System/Defaults/app-defaults.json` — home for `default="zh-CN"`, which lost its
        home in the split. It is a fact ABOUT the set, so no single language's file can own it:
        two of them could each claim to be the default.
  - [x] `Configuration/SystemSettingsPaths` (probes app dir then working dir, as the old single-file
        resolver did) and `Configuration/AppDefaults` (degrades on every failure — startup reads it
        before any window exists, so a throw means a process that dies with no UI to explain itself).
  - [x] `LocalizationService.LoadFromDirectory` — discovery from `*.lang.xml`, no registry anywhere.
        `LoadFromFile` kept for the single-file shape; both share one core.
  - [x] **Key-parity detection** (`KeyGaps`), the condition on which the split was worth doing.
        Reported, not thrown: a translation gap is a defect to fix, not a reason to refuse to start
        in front of a user, and the fallback already renders something readable. The harness is what
        turns the list into a failure.
  - [x] Duplicate language code REFUSED — the likeliest mistake when adding a language is copying
        `en-US.lang.xml` and forgetting the `code` inside, which would silently replace the original.
  - [x] Explicit display ordering (default first, then by code). Discovery order is file-system
        order, and `en-US.lang.xml` sorts before `zh-CN.lang.xml` — the split would otherwise have
        quietly reshuffled the language picker and demoted the default from the top.
  - [x] csproj ships `Settings\**\*`; confirmed the three files land in the build output.
- Notes: build 0/0. Sonar clean on all Phase 1 files. **157 assertions across 5 harnesses, green on
  two consecutive sweeps.**
  - **Chasing a Sonar warning on the JSON DTO uncovered a real bug the test was MASKING.**
    `System.Text.Json` matches property names case-SENSITIVELY by default, so the hand-written
    `"defaultLanguage"` never bound to `DefaultLanguage` — `AppDefaults` was always returning its
    fallback. It passed because the fallback and the file both say `zh-CN`. Fixed with
    `PropertyNameCaseInsensitive`, and the test now uses a value the fallback cannot produce.
    Lesson recorded: **a fixture equal to the fallback proves nothing.**
  - Three harnesses were non-idempotent and failed on re-run looking exactly like regressions:
    shopcheck's save round-trip overwrites the fields its own edit-mode assertions read; headercheck
    inherited shopcheck's leftovers; migcheck asserts a pre-migration schema that its own first run
    destroys. Each now seeds or rewinds its own fixture (migcheck DROPs the columns rather than
    re-copying, so it keeps working once no pre-migration database exists anywhere).

### 2026-07-28 01:30 — Systematic config refactor, PHASE 0: language punctuation is data  [DONE]
- Ask: "系统性的针对整体文件结构优化和调整… Move all static text, content from the code into an
  architectural way… 可以那么按阶段来" (proceed phase by phase)
- Scan first. The premise turned out to be mostly already satisfied: **244 localization lookups in
  C#, 441 bindings in XAML, 986 keys across 24 namespaces.** Of 21 CJK literals in .cs, nearly all
  are COMMENTS. Only 8 user-facing strings are genuinely unlocalized (deferred to Phase 2). The plan
  was rescoped around what is actually broken rather than a wholesale text migration.
- Done (Phase 0 — what actually blocks adding a language):
  - [x] `Format.ListSeparator` / `Format.BulletSeparator` per language, replacing
        `code.StartsWith("zh") ? "、" : ", "` **duplicated across 5 files**. One of them carried a
        comment admitting it had to be kept in step with another.
  - [x] `LocalizationService.JoinList(values)` / `JoinList(values, languageCode)` /
        `JoinFragments(values)`. Exposed as JOINS, not as a raw separator property — handing out the
        separator is what invited five private copies of the rule in the first place.
  - [x] Currency symbols: `CNY ? "￥" : "$"` → a table over `CurrencyType`, with `¤` for an
        undefined value. Falling back to `$` would state something FALSE about an amount.
        Deliberately NOT externalized to JSON: the enum is persisted as integers, so a currency
        cannot be added without a code change anyway, and a JSON file would just be a second place
        that has to agree with the enum.
  - [x] The exported measurements PDF's suffix (`Measurements_zh.pdf`) derived from the BCP-47
        primary subtag instead of `StartsWith("zh") ? "zh" : "en"` — which named every future
        language "en". NOT a Format.* entry: it is the same mechanical rule for every language, and
        derivable data should not be maintained by hand.
- Notes: build 0/0. Sonar: **zero new findings**; every hit in the changed files is pre-existing at a
  line my edits merely shifted. Key parity re-verified: 986 keys, every one present exactly twice.
  - `scratchpad/formatcheck` — **25/25**, no database / credentials / UI involved. The load-bearing
    test ADDS a third language (fr-FR) to a copy of the table and asserts it gets its own
    punctuation: under the replaced rule fr-FR would have rendered `a, b`, because it does not start
    with "zh".
  - **The source-level guard found a 5th site my own scan had missed** —
    `CustomMadeServiceWindow.ShortLanguageName` sniffs the language code with no CJK literal on the
    line, so the CJK grep never saw it. Worth keeping: the guard greps the tree for
    `StartsWith("zh"` and for hard-coded separator literals, so the pattern cannot be pasted back.
  - Spaces in the separators are written `&#32;` in the XML. A trailing space IS the format, and a
    whitespace-trimming editor would silently turn `Jacket, Shirt` into `Jacket,Shirt`.
- Remaining phases agreed with the user: **1** Settings/System/ + split language files +
  auto-discovery + **key-parity validation** (without which splitting is a downgrade, since a missing
  key becomes a silent fallback); **2** the 8 unlocalized strings; **3** the AppData runtime folder
  (flat, 11 doc backups + 12 db backups at the root, no retention) — highest risk, needs migration,
  goes last. **Deferred deliberately:** externalizing `MeasurementTerm.cs` seed data — the ids are
  `const string` referenced by compile-checked code, so JSON would turn compile errors into runtime
  errors, and the per-shop file users already edit is the real config.

### 2026-07-28 00:50 — Shop address in the header; stop seeding "admin"  [DONE]
- Ask: "当前的main application view 的店名下面要标出地址，这样用户就知道操作的是哪个店了。然后当login
  重启时不要透露"admin"作为起始的登录名称"
- Done:
  - [x] `ShopContext.CurrentAddress` + `HasAddress`, mirroring `CurrentName` but with NO fallback
        string — an unset address shows nothing rather than a placeholder.
  - [x] The three notify sites each listed the properties themselves, so a fourth property meant a
        fourth chance to forget one. Factored into `NotifyDisplayChanged()`; the harness asserts all
        three sites raise the whole set, because the failure mode is a header still showing the
        PREVIOUS shop after a switch — which is how an order gets entered against the wrong branch.
  - [x] MainWindow header: address under the name with a map-pin glyph, in the subtle header
        foreground, hidden outright when the shop has none. The header Grid went from ONE cell with
        two overlapping children to two columns — that worked while the left side was a single short
        name, but an address is long enough to run under the right-hand subtitle.
  - [x] Login no longer seeds "admin". The `seedDefaultUserName` parameter is gone entirely rather
        than defaulted to false: with no seeding both paths behave identically (empty box, caret in
        it), so the parameter was dead weight.
- Notes: build 0/0. Sonar: **zero findings in the three changed C# files** (the two `MainWindow.xaml.cs`
  S2325 hits are pre-existing and in a file only its XAML was touched this round).
  - `scratchpad/headercheck` — **29/29, 0 binding errors**. Opens the REAL MainWindow while signing
    NOBODY in: with no current user every capability gates closed and `RefreshSignedInUser` handles
    the null, so the window constructs anyway and the header does not depend on role. Asserts the
    address renders under the title in both languages, is smaller than it, hides for a shop with no
    address, and comes BACK on switching shops. **credentials.json was SHA256-hashed before and
    after and is byte-identical** — the singleton reads it, and the user's app was running.
  - `scratchpad/logincheck` — extended to **26/26**: user name box empty, the string "admin" absent
    from every TextBlock and TextBox on the screen, caret in the name box.
- Flagged to the user: a fresh installation now gives no on-screen hint of the initial account name,
  so whoever sets one up has to be told `admin` out of band. That is the point of the change, but it
  is a real change to first-run.

### 2026-07-28 00:20 — Login screen: stack the language label over its box  [DONE]
- Ask: "优化一下login的页面， 把语言选择和label上下摆放"
- Done:
  - [x] The language row was `Orientation="Horizontal"` with a 12px muted label beside a fixed
        150px box, which read as a footnote hanging off the password field. Now stacked
        label-over-box like the two fields above it, sharing `FieldLabelStyle` and running the full
        362px column width — a third field that looks like one.
  - [x] New `FieldComboStyle` (`BasedOn` ThemedComboBox), matching the 15px size-up treatment.
  - [x] Window 560 → 600 to keep room for the error message, which appears in the same column and
        pushes the language field down. Verified with a doubled (3-line, 48px) error still clearing
        the Sign in button.
- Notes: build 0/0, XAML only (no C#, so nothing for Sonar). `scratchpad/logincheck` —
  **20/20, 0 binding errors**, both languages, asserting the stack by geometry: panel Orientation
  is Vertical, label bottom (372) ≤ box top (377) so they are not side by side, box shares the
  username box's left edge and width (x 32 = 32, w 362 = 362), and the label matches the other
  field labels (13/SemiBold).
  - **A TextBox and a ComboBox do NOT measure to the same height from the same font and padding**,
    and I got the direction backwards twice before dumping the tree. Details in context.md. Fixed
    by pinning a shared `FieldHeight` so the three are equal by construction.
  - Reaching for the visual-tree dump earlier would have saved two wrong guesses — the arithmetic
    from the XAML looks obvious and is not what the layout system actually does.

### 2026-07-27 23:40 — Shop contact details (address / phone / email)  [DONE]
- Ask: ">给每个店的信息增加一个地址选项，email 和联系方式 等等 / 编辑菜单在店名底下添加小字Address作为标识。"
- Done:
  - [x] `Shop.AddressesJson` per language (mirrors `NamesJson`) + `Addresses` / `SetAddresses` /
        `ResolveAddress`; `PhoneNumber`, `Email`, `Website` as plain nullable strings. The JSON
        decode and the language fallback were factored into `DecodeLocalized` / `Resolve` rather
        than copied, so name and address cannot drift.
  - [x] Schema: 4 columns added to BOTH the `CREATE TABLE` in `EnsureShopSchemaAsync` AND
        `ShopColumnMigrations` — the file's own comment warns that doing one and not the other
        works on exactly one kind of installation. Both branches are covered by the harness.
  - [x] ShopSetupWindow: address block directly under the shop name in the same per-language shape;
        phone / email / website in a three-column row below. The per-language row template was
        extracted to a shared `LocalizedFieldRow` DataTemplate and `ShopNameEntry` renamed
        `LocalizedTextEntry`, since both editors are the same row with a different label.
  - [x] `Languages.xml`: 7 new keys × 2 blocks; `Shop.Setup.Subtitle` updated, since it enumerates
        what the page configures. Verified 491 keys per block, every key present exactly twice.
- Notes: build 0/0. Sonar run with the analyzer package: **zero findings in the three changed C#
  files**. (31 pre-existing findings elsewhere in the codebase under SonarAnalyzer 9.x — mostly
  S6602/S6603/S6605 "use the collection-specific method", which are newer rules than earlier
  sessions ran. Not touched: out of scope.)
  - `scratchpad/migcheck` — **25/25 against a COPY of the live orders.db**, calling the real private
    `App.EnsureShopSchemaAsync` by reflection rather than re-typing the DDL (a copy of the SQL would
    pass while the shipping code was wrong). Covers: upgrade path preserves all 3 shops and every
    `NamesJson` byte-for-byte, new columns read NULL, a SECOND run is a no-op (startup repeats it on
    every launch, so a duplicate-column crash would brick the app after one restart), fresh-install
    CREATE TABLE carries the columns, and a full round trip.
  - `scratchpad/shopcheck` — **31/31, 0 binding errors**, opening the real ShopSetupWindow in both
    languages and both modes against the migrated copy. Layout asserted by GEOMETRY, not screenshot:
    address block below the name block (191→335), same left edge and width as the name boxes
    (x 145 = 145, w 679 = 679), same 38px height as the contact boxes. The save path is driven
    through the real handler and read back from the database — trimming and whitespace→NULL included.
- Decisions:
  - Address is PER LANGUAGE, phone/email/website are not. Follows the reasoning already in the
    codebase: shop names are per language because they are printed and shown on screen, while
    `ReceiptBrandingSettings.TaxRegistrationNumber` is deliberately single-valued because "a
    registration number is the same string whoever is reading it". An address reads differently in
    Chinese and English; a phone number does not.
  - NOT auto-injected into the printed receipt. The receipt header/footer is already free rich text
    per language, so a shop that wants its address printed has typed it there — injecting would
    double-print. Left as a follow-up for the user to direct.
  - `Website` is the "等等" slot, flagged to the user rather than added quietly.

### 2026-07-27 23:10 — Nav bar order + right-click menu theming  [DONE]
- Ask: "Switch the language toggle section with Local configuration. on the nav bar" /
  "根据当前的主题，再去优化一下主界面右键唤出的tooltip ui"
- Done:
  - [x] System bar columns swapped: Local Configuration 3→1, language label 1→2, language box 2→3. Order is now
        greeting · Local Configuration · Language · Store Members · Sign Out. Right margin 14→18 on the menu, because its
        neighbour changed from a padded button to a bare text label and 14 read as cramped.
  - [x] **The right-click menu was running on stock Windows chrome — same missing-`BasedOn` fault as
        the login inputs, third occurrence.** `OrderContextMenuStyle` and `OrderMenuItemStyle` in
        MainWindow.xaml had no `BasedOn`, so they REPLACED the implicit theme styles: the six items
        fell back to the stock MenuItem template while the menu bar overhead used `ThemedMenuItem`,
        and the ContextMenu style dropped `Grid.IsSharedSizeScope`. Both styles deleted outright so
        the implicit styles apply.
  - [x] New `ThemedContextMenu` in AppTheme: real ControlTemplate, 8px radius + `DropShadowEffect`
        matching the menu-bar submenu popups exactly. Templated, not merely recoloured — the stock
        ContextMenu's square Border and legacy offset-rectangle shadow cannot be set away.
  - [x] New keyed `{x:Static MenuItem.SeparatorStyleKey}` style. A Separator inside a menu never
        reaches the implicit `Style TargetType="Separator"`.
  - [x] New `DangerMenuItem` — delete now reads red at rest AND while highlighted.
- Notes: build 0/0; 2 files changed, both XAML, no C# — nothing for Sonar (a C# analyzer) to inspect.
  - Verified by `scratchpad/menucheck`, a THEME-ONLY harness: **35/35 assertions, 0 binding errors,
    3 consecutive runs.** It deliberately touches no database and no credentials file, because the
    user's application was running against both; the full `uicheck` sweep was not run for that reason.
  - Measured, not eyeballed: label gutter spread **0px across all 6 items** (the shared-size scope
    doing its job), separator hairline 1px `#FFE5E7EB`, delete `#FFB91C1C` at rest and highlighted,
    plain item `#FF3730A3` highlighted, corner alpha 1/255 (opaque would be 255).
  - The harness was flaky on first write — a ContextMenu closes when its window loses foreground, and
    one run lost it, taking 3 assertions down. Fixed with an activate-and-retry loop rather than
    accepting the flake.

### 2026-07-27 22:20 — Login inputs, date picker sizing and calendar theme  [DONE]
- Ask: "》优化登录主界面的input, 没有加载theme的input 》优化date time picker, date time picker 应该保持跟主色调
  一致的字体和颜色，现在字体太小。并且要跟date picker input的长度一致，如果做不到一致，那就整个选择面板右对齐。
  》然后datetime picker input height，要与面板内的 同一行的 input等高。"
- Done:
  - [x] **Login inputs were unthemed because an explicit Style REPLACES the implicit one.**
        `FieldInputStyle` (TargetType=Control, no BasedOn) opted both boxes out of the theme entirely.
        Split into `FieldTextBoxStyle` / `FieldPasswordStyle`, both `BasedOn` the themed styles. The
        same fault was found and fixed in `ShopSetupWindow` (`InputStyle`, `PickerStyle`).
  - [x] LoginWindow and LanguageSelectionWindow were never in the original palette sweep — swept now.
  - [x] Input heights: TextBox applied its `Padding` TWICE (the text host already honours it; the
        template also set it as the content host's `Margin`), making text boxes 47px against the
        picker's 33px. Fixed at the cause, then MinHeight 38 pinned on TextBox / PasswordBox /
        ComboBox / DatePicker so a row lines up. Verified by measurement: **38 = 38**.
  - [x] Calendar now matches the picker's width exactly (287px, not the stock 179), at 13px instead
        of 10, with the primary colour on today and on selection, and muted adjacent-month days.
- Notes: build 0/0, Sonar zero findings, 28 window opens with 0 binding errors.
  - **The whole Calendar style was silently inert until it was named on `DatePicker.CalendarStyle`.**
    DatePicker BINDS its Calendar's `Style` to that property, and a bound null Style suppresses
    implicit-style lookup — so the implicit `Style TargetType="Calendar"` never applied, with no
    binding error to show for it. Same for the day buttons via `Calendar.CalendarDayButtonStyle`.
    Diagnosed by asserting in the harness (`dayButtonStyle=null day=stock fontSize=10`) rather than
    by staring at screenshots, which had already misled me twice on this one.
  - Calendar WIDTH could not be done in XAML at all: the Calendar is created in code inside a Popup,
    a separate visual tree, so `RelativeSource AncestorType=DatePicker` finds nothing — silently.
    `Controls/CalendarSizing.cs` sets it on Loaded/SizeChanged instead, which is before the first
    open, so there is no wrong-width frame. The user's right-align fallback was not needed.

### 2026-07-27 21:55 — Reverted the mirrored menu; swapped Local Configuration and Store Members instead  [DONE]
- Ask: "It doesn't look good. now, lets roll back the original version of the caret right and content
  left. but switching the position for store members and local configuration. that will be better"
- Done:
  - [x] `RightAlignedMenuItem` deleted; the Local Configuration menu is back on the shared `ThemedMenuItem` —
        content left, caret right, submenus opening rightward.
  - [x] Local Configuration moved to column 3 and Store Members to column 4, so the bar reads
        greeting → Language → Local Configuration → Store Members → Sign Out.
- Notes: the swap is the better fix and makes the mirrored style unnecessary. A drop-down opens
  down-and-LEFT from its item, so a menu at the extreme right edge fights the window boundary;
  keeping a button to its right gives it room to open normally. Position solved what a mirrored
  template was compensating for.

### 2026-07-27 21:45 — Right-anchored Local Configuration menu  [REVERTED — see the entry above]
- Ask: "UI的一个小问题：现在local configuration 靠右，那么需要把所有选项右对齐，然后expandable icon 要caret left"
- Done:
  - [x] `RightAlignedMenuItem` in the theme — the mirror of `ThemedMenuItem`: labels right-aligned,
        icon column on the right, expander caret on the LEFT pointing left, submenus opening leftward
        (`Placement="Left"`). Applied to the Local Configuration item only.
  - [x] It hands itself down the tree via `ItemContainerStyle="{DynamicResource RightAlignedMenuItem}"`
        — a self-referencing style needs Dynamic, since a StaticResource cannot name the style being
        declared. Without it only the first level of items would mirror.
- Notes: opt-in per menu ON PURPOSE. The orders row's context menu opens at the pointer on the left
  side of the window and keeps the normal left-aligned style; a global flip would have broken it.
  - Verified by rendering the popup CONTENT: a Popup lives in its own window, so it never appears in
    the parent window's `RenderTargetBitmap` — but `popup.Child` is an ordinary visual and renders on
    its own. Worth remembering for any future menu or drop-down screenshot.

### 2026-07-27 21:20 — Panel open/close transition + modular typography  [DONE]
- Ask (three messages, one thread): "Add a open/close panel global 0.5s easin easeout non linear
  transition" → "然后，查看一下整个系统字体是否乱用，保证字体的一致性" → "字体需要模块化，根据不同情况，使用不同的字体，请自行斟酌"
- Done:
  - [x] `Animations/PanelTransition.cs` (NEW): attached `Mode` property (None / Fade / FadeSlide),
        0.5s, `CubicEase` EaseInOut, 10px slide. Duration and curve live in ONE place.
        Applied to 20 panels — the three swap panels in each management window, the roster's
        notice/editor/deactivated/password cards, the picker's empty state, and the order editor's
        service, pricing and final-payment blocks.
  - [x] Font audit: **17 sizes in use**, including 11.5 / 12.5 / 13.5 / 14.5 — differences too small
        to read as intent. Collapsed to a **six-step scale** (11 / 12 / 13 / 15 / 18 / 22), covering
        both the attribute form and the `<Setter Property="FontSize">` form.
  - [x] Modular typography: THREE families by job — `AppFontFamily` (Segoe UI, Microsoft YaHei UI),
        `NumericFontFamily` (same face, tabular numerals), `IconFontFamily` (Segoe MDL2 Assets) —
        plus semantic styles (`PageTitleText` / `SectionTitleText` / `BodyText` / `ValueText` /
        `CaptionText` / `NumericText` / `MoneyText` / `NumericCellText` / `IconGlyph`).
  - [x] The orders list's order-number, phone, date and amount columns converted from
        `DisplayMemberBinding` to a cell template using `NumericCellText`, so figures line up down
        the column. All 21 hard-coded `FontFamily="Segoe MDL2 Assets"` now reference the theme.
- Notes: build 0/0, Sonar zero findings, 24 window opens with 0 binding errors.
  - **The transition is binding-safe and re-entrancy-safe, and both took thought.** It never assigns
    Visibility (that would replace a `{Binding}` permanently); the closing half animates Visibility
    with a key-frame track, which borrows the property and hands it straight back. And because that
    track re-shows the panel at t=0, it re-raises IsVisibleChanged — guarded, with the guard cleared
    one dispatcher turn AFTER completion so the hand-back is suppressed too. Verified by a harness
    test that pumps real time past 0.5s and asserts the end state in both directions.
  - `NumericCellText` deliberately sets NO size and NO colour: a list row takes its size from the
    font-size slider and its colour from the completed/refunded gray-out trigger.
  - Sonar S3220 on `new PropertyPath(x)` — it sits between `PropertyPath(object)` and
    `PropertyPath(string, params object[])`. Resolved by a `TargetPath` helper that names the string
    overload explicitly, rather than by suppressing it at three call sites.

### 2026-07-27 20:55 — System-bar order, records width, balance column, list font  [DONE]
- Ask: "整体做的很好，有个小问题，balance status改成原有的两倍长最好，然后Hi admin, you are login....这个应该放在最左边，替换local configuration 的位置，然后local configuration 放置再store members button 和signout button 之间。然后整体的records section 把界面宽度提升到70%， balance status改成1.5倍宽度，然后字体改成18号字体。"
- Done:
  - [x] System bar reordered: greeting far left, then Language → Store Members → Local Configuration → Sign Out. Laid out in
        GRID COLUMNS rather than two aligned stacks, so `Grid.Column` sets the visual order and the
        ~90-line Local Configuration menu block stays where it is in the file instead of being moved.
  - [x] Records column 65* → **70***, detail 35* → 30*.
  - [x] Balance-status column 180 → **270 (1.5×)**; record list font 20 → **18**.
- Notes: the ask names the balance column twice — "两倍长" first, "1.5倍宽度" later. Took the later,
  more specific figure (1.5×) and said so; changing it is one number.
  - The 1600-wide harness screenshot still clipped that column, which reads as "too narrow" but is
    the VIEWPORT, not the column — re-rendered at 2200 (the user runs maximized at 2560) and both
    Order.Fields.BalanceStatus and Custom Service render in full. Worth remembering before "fixing" a width from a screenshot
    taken at the wrong size.

### 2026-07-27 20:10 — Seeded garments bug, dropdown/menu theme, navigation split  [DONE]
- Ask: "Bug: You have inserted a bounch of records, for instance: Order number 20260723-192307 in Guangzhou Tianbao store, it has Custom Service, it should state as YES (Jacket, Shirt) as we designed. I created a similar order number 20260727-193940, please adjust all orders for all records. Features TODO (UI update): >maintain the theme UI. >Adjust UI. for all dropdowns, follow the same theme, Local Configuration should follow same theme as well. >Split Navigation in to two differnent section. -Now, order adjustments such as New order, edit order, delete order, refresh, should be inside the main record section. -Redesign this section, padding and margin is reasonable. -The rest selections are system related. reorder like this: ->Hi, {client's real name}, you are logged in as {role}. ->After that has language toggle, if it has ->after button Store Members ->Remove the current label for format like admin(Administrator) ->Sign Out"
- **Bug root cause (found before planning):** `Order.HasCustomMadeService` and
  `CustomMadeMeasurementReader.GetGarmentNames` both read `record.Garments` — the garment-driven model.
  The seeder only filled the LEGACY `JacketLengthCm` / `ShirtChestCm` fields, which migrate into
  `Garments` only when the record is re-saved through the editor. So every seeded custom-made order
  reports `CustomMade.Flag.No`. My bug, in the mock data, not in the flag.
- Plan:
  - [ ] Repair pass over every seeded record: build `Garments` (jacket / shirt / dress / qipao by age
        type) with real term ids, so the flag and the bracketed names resolve.
  - [ ] ComboBox: full themed template this time, handling BOTH `IsEditable` and `DisplayMemberPath`
        (the earlier attempt was reverted for exactly those two — RelativeSource instead of
        TemplateBinding is the fix for the face).
  - [ ] Theme the Local Configuration `Menu` / `MenuItem` / `ContextMenu` / `Separator`.
  - [ ] Split the navigation: order actions (Add / Edit / Delete / Refresh) move into the orders panel's own
        action bar; the toolbar keeps Local Configuration plus the system block, reordered to
        greeting → language → Store Members → Sign Out, with the `admin（管理员）` chip replaced by
        "Hi, {name}, you are logged in as {role}".
- Done:
  - [x] Every seeded custom-made record repaired in place — `Garments` built from the predefined
        garment/term ids (menswear vs womenswear by age type). 13 + 17 + 7 orders fixed; the Custom Service
        column now renders **有（外套、衬衫）/ YES (Jacket, Shirt)** as designed, verified by resolving
        the names through `CustomMadeMeasurementReader` with the string table loaded.
  - [x] ComboBox fully themed at last. **The bit that had defeated two attempts:** a ComboBox does
        NOT turn `DisplayMemberPath` into `SelectionBoxItemTemplate` — `ItemsControl` installs an
        internal template SELECTOR, so the face must also bind
        `ContentTemplateSelector="{Binding ItemTemplateSelector, …}"`. Without it the face falls back
        to `ToString()` and shows `LanguageOption { Code = …, Name = 简体中文 }`. Also added
        `PART_EditableTextBox` + an `IsEditable` trigger, so the branding editor's font-size box still
        accepts typing.
  - [x] `Menu` / `MenuItem` / `ContextMenu` / `Separator` themed: one MenuItem template covering all
        four roles, with the submenu arrow and popup placement driven by `Role` triggers.
  - [x] Navigation split. The ToolBar is gone — it cannot right-align content and adds an overflow
        chevron. A `Border` + `Grid` now holds Local Configuration on the left and, on the right in the order asked
        for: greeting → language → Store Members → Sign Out. The `admin（管理员）` chip is replaced by
        `Main.Greeting` ("你好 {0}，您当前的身份是{1}。"), which greets by DISPLAY NAME where the account
        has one — an account name is what you sign in with, not what anybody calls you.
  - [x] Add / Edit / Delete / Refresh moved into a new records-panel action bar (white card, 16,13 padding, count
        badge bound to a new `MainViewModel.FilteredCount`). Order-column rows renumbered 0-3 in the
        same edit as the new `RowDefinition` — per the SKILL gotcha, an out-of-range `Grid.Row` is
        clamped silently rather than reported.
  - [x] `OrderEditWindow`'s local implicit `TextBox` style now derives from `ThemedTextBox` instead of
        shadowing it — the editor holds most of the app's inputs, so a bare implicit style there
        quietly opted the biggest screen out of the theme.
- Notes: build **0 warnings / 0 errors**; Sonar **zero findings**; string table 485 keys per block,
  identical. 22 window opens across both languages with **0 binding errors**, including the order
  editor and the main window, both reviewed as screenshots.
  - `Toolbar.SignedInAs` pruned (orphaned by the greeting); `Main.Greeting` + `Main.Records` added.

### 2026-07-27 19:05 — App-wide theme, receipt layout, mock data  [DONE]
- Ask: "目前来说改动很不错。下一步: >Redesign 所有的 Theme, 根据之前所用的比如，theme颜色， button那些，需要重新设计 -main application UI -Order (edit/view) -店铺设置UI（这个还可以，但是主色没有添加）-量身项目设置的theme也同步一下 -货币设置可以从navigation去掉了，现在已经在店铺设置里有了，多余了 >订单打印的PDF 文件结构和UI也优化一下，注意留好spacing和padding 和margin。>最后给当前的db添加一些mockingdata，再添加一个店，并且给 每个店添加30 到50个records不等。"
- Reading of the ask:
  - The management screens built earlier (Select Shop / User Management / Store Members) already carry the target design
    language — indigo #4F46E5 → violet #7C3AED, white cards on #F4F5F9, rounded buttons. "Redesign 所有的
    Theme" means bringing the REST of the app onto it, not inventing a third look.
  - The legacy palette is dominated by #2980B9 / #2C3E50 (27 + 11 uses) plus a spread of near-identical
    greys and borders; those are chrome and get mapped. Status colours that ENCODE MEANING (balance
    status green/orange/red, the refund strike) are left semantic.
- Plan:
  - [ ] `Themes/AppTheme.xaml`: palette brushes + implicit control styles, merged in `App.xaml` so every
        window inherits. Fold `Views/ManagementStyles.xaml` into it.
  - [ ] Map the legacy chrome colours onto the palette across MainWindow / OrderEditWindow /
        ShopSetupWindow / MeasurementTermsWindow / CustomMadeServiceWindow / ReceiptBrandingWindow.
  - [ ] Shop Settings gains the gradient header the other screens have.
  - [ ] Drop Currency Setup from the Local Configuration menu (superseded by Shop Settings) and prune what it orphans.
  - [ ] Rework the printed receipt's structure and spacing.
  - [ ] Seed one more shop and 30–50 orders per shop into the live database (back it up first).
  - [ ] Both gates green; build clean; every window opened and screenshotted.
- Follow-up asked mid-task: "员工管理的date time picker缺少Localization。并且time picker界面太单调和难看。
  优化一下，采用主色调。" — folded into the theme work.
- Done:
  - [x] `Themes/AppTheme.xaml` (NEW), merged in `App.xaml`: the palette as named brushes plus implicit
        styles for Button / TextBox / PasswordBox / ComboBoxItem / DatePicker / CheckBox / RadioButton,
        and the keyed card / heading / label / roster-row styles. `Views/ManagementStyles.xaml` folded
        into it and deleted.
  - [x] Legacy palette mapped onto the theme across nine XAML files — 92 lines in MainWindow +
        OrderEditWindow alone, all of them colour-only (`git diff` shows zero changed lines containing
        CJK). #2980B9/#2C3E50 → the indigo primary, the six near-identical greys → three, the eight
        near-identical borders → one.
  - [x] MainWindow: gradient header band, indigo list headers with a readable sort glyph, primary-tinted
        row selection, white toolbar and status bar. Shop Settings gained the same gradient header.
  - [x] **DatePicker localized**: the stock "Select a date" watermark comes from PresentationFramework
        and stays English whatever the app language is, so `DatePickerTextBox` is re-templated with a
        `Common.SelectDate` watermark; each window carrying a picker sets `FrameworkElement.Language`
        so the calendar's month and day names follow the UI language too.
  - [x] **Time picker redesigned**: `TimePickerComboBox` — clock glyph, primary tint, primary drop-down
        with a coloured shadow. `TimeOption.ToString()` was needed because a custom ComboBox face
        renders the ITEM rather than resolving `DisplayMemberPath`.
  - [x] Currency Setup removed from Local Configuration (it is part of Shop Settings now); `CurrencySettingWindow` deleted and
        its two orphaned keys pruned. `Toolbar.CurrencySetting` KEPT — the global-settings package
        description still names it.
  - [x] Receipt restructured: customer block and totals block are now padded panels (the totals one
        tinted, with a heavier top rule), section titles are primary-coloured, line leading 1→3px, page
        padding 40 → 48/40, and the payment-or-refund narrative moved out of the totals panel into its
        own helper so the block the eye lands on stays money only.
  - [x] Mock data seeded through the app's own model/formatter: a third shop (Vancouver Atelier, daily
        sequential numbering) plus 38 / 44 / 30 orders per shop, spread over eight months, with a mix of
        services, statuses, payment methods and refund reasons. **0 orders with no shop.**
- Notes: build **0 warnings / 0 errors**; full Sonar pass **zero findings**. String table 484 keys per
  block, identical, no duplicates. 20 window opens across both languages with **0 binding errors**;
  the main window, the roster, the pickers and a rendered receipt were all reviewed as screenshots.
  - **A hand-rolled ComboBox ControlTemplate was reverted on purpose** — see context.md. It broke
    `DisplayMemberPath` on the selection face app-wide and would have broken `IsEditable`. Generic
    drop-downs keep the stock template and get the theme through setters + the `ComboBoxItem` style.
  - Live database backed up to `scratchpad/orders.pre-mock.db` before seeding (53 KB, 16 real orders,
    all preserved).

### 2026-07-27 18:15 — Store members panel (per-shop membership, activation, schedule)  [DONE]
- Ask: "Use the similar UI as above to design and do features: Manage store members in current opened store. >Manager is able to view all active users in the store. >Can see how many workers in the store. >What roles are they currently are >Active user control. if ever active, but deactive, next time even he login with the right auth, it should say something like you are not valid to login, something like that, or your account is deactivated. >But if he belongs to another active store, he can still login to view the store. Admin user in current opened store. >Including all features for manager's role. >Add delete user ability. The Management panel is called by a link with beautified icon on the main application. >trigger will open a beautifed UI with all users. >Besides the regular info(name, birthday), also include the time schedule they worked from and until, when they started work and if deactivated, show when delisted from the role. show a timepicker for that. >Manager can add new user to manage the store, plus create username and password, role in the store for the user. >Manager can create a new user as manager too. can also demote him/her to staff."
- Reading of the ask, taken as assumptions:
  - **Activation is PER SHOP, not per account** — that is the only reading under which "if he belongs to
    another active store, he can still login" is true. Sign-in is refused only when EVERY membership is
    deactivated.
  - "time schedule they worked from and until" + "show a timepicker" = the daily shift (two times of day);
    "when they started work" = the date they joined this shop; "when delisted" = a timestamp stamped
    automatically on deactivation, never typed.
  - Name and birthday are ACCOUNT-level (a person has one birthday); shift, join date and activation are
    MEMBERSHIP-level (the same person can work different hours at two branches).
- Plan:
  - [ ] Model: `ShopAssignment` (shop, role) becomes `ShopMembership` (shop, roleS, active, joined,
        deactivated, shift) — one record per person per shop. Credential-file schema version 3.
  - [ ] `AuthenticationService`: active-only role resolution, a sign-in failure reason for a fully
        deactivated account, member CRUD scoped to one shop, `CanManageStoreMembers`.
  - [ ] `LoginWindow` reports "account deactivated" distinctly from bad credentials.
  - [ ] New `Views/StoreMembersWindow` in the same visual language as User Management.
  - [ ] Toolbar icon button on `MainWindow`, gated to manager + administrator.
  - [ ] `UserManagementWindow` updated to the membership model, preserving per-shop metadata on save.
  - [ ] Localization keys in both blocks; both gates green; build clean; verify by execution.
- Done:
  - [x] `ShopAssignment` (shop, role) → `ShopMembership` (shop, **roleS**, `IsActive`, `JoinedOn`,
        `DeactivatedOn`, `ShiftStart`/`ShiftEnd` as `TimeOnly?`), one record per person per shop.
        `CredentialRecord` gained `DisplayName` + `BirthDate` — a birthday is the same in every branch,
        a shift is not, which is exactly where the account/membership line falls.
  - [x] Credential file schema **version 3**, upgraded from BOTH earlier shapes. The version-2 fold
        (flat assignments → one membership per shop) needs no shop list and runs on load; the version-1
        step still defers to `ApplyLegacyShopMemberships`. The version is only bumped once no record is
        still waiting for a shop list, which is what lets the two halves coexist.
  - [x] `Authenticate` now returns `SignInResult` with a `SignInFailure`. A correct credential is
        refused ONLY when the account belongs to ≥1 shop and every membership is deactivated — being
        suspended at one branch must not cost someone their job at another. An account with NO
        memberships is deliberately not "deactivated": that is a new hire, and they get the accurate
        "no shop is available" message instead.
  - [x] NEW `Views/StoreMembersWindow`: head-count tiles (total / active / deactivated), roster cards
        with role + shift and an Active/Deactivated badge, and an editor for person (name, birthday),
        role in THIS shop (manager and/or staff), activation, start date, read-only delisting stamp
        and a 15-minute shift picker. Add-member creates the account and its membership together.
        Delete Account is administrator-only; a manager's tool for "they left" is deactivation, which records
        when.
  - [x] `Views/ManagementStyles.xaml` (NEW): the shared card/button/input language, merged by all
        three management windows. Extracted the moment a third window needed it.
  - [x] Toolbar entry point on `MainWindow` — a vector two-person icon + label, gated by the new
        `CanManageStoreMembers`; `CanDeleteAccounts` gates deletion.
  - [x] `UserManagementWindow` moved onto memberships: its matrix now sends ROLES ONLY
        (`SetShopRoles`), so editing an account there cannot silently reset the roster's activation,
        start date or shift.
  - [x] 34 keys × 2 blocks; both blocks verified at **485 keys, identical sets, no duplicates**.
- **Verified by execution:**
  - `authcheck`: **52/52**. Covers the deactivation rules end to end (delisted everywhere → refused;
    delisted here but active there → signs in and sees only the other shop; reactivation clears the
    stamp), the roster CRUD, promote/demote, both legacy upgrades on synthetic files, and the guards
    (manager cannot deactivate themselves in the open shop, cannot delete accounts, cannot reset the
    password of someone who also works in a shop they do not run). Live file restored byte-identical.
  - `uicheck`: 16 window opens across both languages, **0 binding errors**, screenshots reviewed.
    Two real bugs were found from the screenshots and fixed: the footer Save Changes / Delete stayed enabled
    while the add form was open (they still pointed at the previously selected row — in BOTH windows).
- Notes: build **0 warnings / 0 errors**; full Sonar pass **zero findings**, no suppressions added.
  - **The live `credentials.json` was already at version 2** when this work started — the app had been
    signed into since the previous task. The harness assertions were rewritten to check the END STATE
    rather than assume a starting version, with the version-1 path covered on a synthetic file instead.
    Worth repeating: a test that depends on how many times real data has already been migrated is a
    test that fails for the wrong reason.
  - **IDE will need a language-server restart** (SKILL §15): `StoreMembersWindow.g.i.cs` does not exist
    yet and `MainWindow.g.i.cs` is one field behind (18 vs 19 — missing `StoreMembersButton`), because
    a new XAML window and new `x:Name` controls were added. `dotnet build` is clean, so any CS0103 in
    the editor is the stale design-time model, not a defect.

### 2026-07-27 17:40 — Per-shop user & role management  [DONE]
- Ask: "Implement new features: User's rule management System >Admin can access all resources for the application >Admin can assign any user with sepcific role and assign him/her into any store. >Admin user is currently locked, no one else can be assigned as admin. >The user can be assigned in different stores at the sametime. >User can be manager/staff role in any store. for example, User1 can be manager in store 1 and both in store 2. >no limitation of rules for now in any store. >Now, lets create two empty accounts without any roles. User1: test1 test1 User2: test2 test2 >Create and beautify UIed panel to manage user, manage user roles can be on the Select shop step. Have this section UI redesigned. >Manager/staff can switch different stores (if they have the ability to view), but only manager can modify the store's setting. Local configuration setup is limited. -Import/export,Local database is disabled for both roles, do not expose the info at the bottom as well. -Set Header & Footer is unviewd by staff -Shop settings, measurement terms and currency setup is unviewed by staff"
- Reading of the ask, taken as assumptions (stated to the user, not blocking):
  - "manager in store 1 and **both** in store 2" = a (user, shop) pair may hold Manager **and** Staff at once, so
    an assignment is a SET per shop, rendered as two checkboxes per shop row. Effective capability = the highest
    role held there.
  - Admin is an ACCOUNT-level flag, not a per-shop assignment ("access all resources"). The `admin` account is
    locked: it cannot be deleted or demoted, and no other account can be promoted to it.
  - Creating a shop stays administrator-only — the ask grants a manager the right to *modify* a store's settings,
    not to add branches.
- Plan:
  - [ ] `AuthenticationService` v2: per-shop assignments keyed on `Shop.PublicId`, `IsAdministrator`, account
        CRUD, a seeded-account marker so a deleted account stays deleted, legacy-role migration.
  - [ ] Capability API resolved against the ACTIVE shop, bound in `App.ApplyActiveShop`.
  - [ ] Shop lists filtered by assignment (startup path + picker + Switch Shop).
  - [ ] `ShopPickerWindow` redesigned; User Management launches from it (admin only).
  - [ ] New `Views/UserManagementWindow` — user list, add/delete, password reset, shop×role matrix.
  - [ ] `MainWindow` chrome gated by role and RE-APPLIED on every shop switch; status-bar database path hidden
        for non-administrators.
  - [ ] test1/test2 accounts seeded with no roles.
  - [ ] Localization keys in both blocks; both gates green; build clean.
- Done:
  - [x] `AuthenticationService` rewritten around `IsAdministrator` + `ShopAssignment[]` keyed on
        `Shop.PublicId`. Capabilities split from the old single `CanManageShops` into `CanCreateShops` /
        `CanManageUsers` / `CanUseDataTools` (administrator, whole-installation actions) and
        `CanConfigureShop` (administrator or the OPEN shop's manager). `BindShop` supplies the shop the
        answers resolve against, called from `App.ApplyActiveShop` **before** `SetActive`.
  - [x] Credential file is now version 2, upgraded in TWO steps because the service is constructed for
        the login window — before the host exists and therefore before a shop can be read.
        `UpgradeAccountShape` (on load) turns a global `Role=Admin` into the admin flag;
        `ApplyLegacyShopAssignments` (called from `App` after the shop bootstrap) turns a legacy
        Manager/Staff into that role in every shop that exists, preserving what those accounts could
        already open, and refreshes the already-signed-in session so the user is not told "no shop is
        available" one second before being granted access.
  - [x] `CredentialFile.ProvisionedAccounts` records what has ever been seeded, so a deleted account
        stays deleted. Seeding an account on every load was the old behaviour and would have made the
        new delete button useless. `admin` is exempt — an installation with no administrator can never
        be administered again.
  - [x] `ShopPickerWindow` redesigned: gradient header with the signed-in chip, shop CARDS with an
        avatar tile, per-shop role badge and hover/selected accent, and a footer carrying User Management
        (administrators only). Two distinct empty states — "no shops exist" vs "none is yours".
  - [x] NEW `Views/UserManagementWindow`: account list (search + avatar + role summary + Locked badge),
        create panel, password reset, and a **shop × role matrix of checkboxes** — which is what makes
        "manager AND staff in the same shop" expressible rather than an either/or dropdown.
        Archived shops still appear, or saving an account would silently strip an assignment to one.
  - [x] `MainWindow.ApplyRolePermissions` extended and, critically, **re-run on `ShopContext.ShopChanged`**
        (and after User Management closes). It was construction-only, so a manager who switched into a shop where
        they are staff kept the manager menus.
  - [x] Defence-in-depth guards on all 14 gated handlers; status-bar database path hidden with the
        data tools it describes.
  - [x] 24 keys × 2 blocks; both blocks verified at **452 keys, identical sets, no duplicates**.
- **Verified by execution, not just by build** (two scratch harnesses referencing the built dll):
  - `authcheck`: **54/54 assertions**, run against the LIVE credentials file so the version-1 upgrade was
    exercised on real data — legacy manager gains Manager in both real shops, admin gains none, a second
    migration pass is a no-op, both roles in one shop resolve to Manager while BOTH are persisted,
    re-assignment revokes the old shop, staff cannot configure, the administrator cannot be deleted or
    assigned, a deleted seeded account is not resurrected, and a missing administrator is restored.
    The file was restored byte-identical afterwards (sha256 unchanged), so the real migration still
    happens on the user's next sign-in.
  - `uicheck`: opens the redesigned picker and the new user-management window **for real** in both
    languages, with a `PresentationTraceSources` listener attached — 8/8 windows opened, **0 binding
    errors or warnings**. A XAML resource/template mistake compiles cleanly and only fails on show, so
    a green build says nothing about whether these screens open. Screenshots were captured and reviewed;
    two wording bugs were found that way (a duplicated prompt, and an empty-state message that blamed
    the wrong cause) and fixed.
- Notes: build succeeded, **0 warnings / 0 errors**, and a full build with `SonarAnalyzer.CSharp`
  temporarily referenced reported **zero Sxxxx findings** project-wide (package removed; `git diff` on the
  csproj is clean). No `[SuppressMessage]` was needed: the four S2325 flags on the status-message helpers
  were resolved by restructuring them to take a string-table KEY instead of a finished string, so they
  genuinely read instance state.
  - `.g.cs` / `.g.i.cs` field counts match on all three touched windows (21/21, 9/9, 18/18) — no stale
    design-time model this time, unlike the previous session.

### 2026-07-27 16:40 — Store-scoped payment/tax rules, split card types, receipt number format, GST/HST  [DONE]
- Ask: "Implement new features: 1.Apply new rules for Stores > List all types of payment types for the charging tax. basic diagrams like below / TAX free / Tax Rate / CASH (Check mark) / Card / Debit Card (Check mark) (inputfield - a changable field) / Credit Card (Check mark) (inputfield - a changable field) / Etransfer (Check mark) / You can save this settings in the global setting for the shop respectively. 2. Beautify the above UI, make it morden look. easy to access. 3. Divide Card type into two separate groups, Debit and credit card. 4. Apply the rules for the payment areas, and locking the tax percentage area - Making the inputbox in text lable, bolded. > the change on the global tax percentage charging rules will reflect globally in store's scope > By default Cash + Etransfer are 0%, any card type is 13% 5. Add Rules for Store's Receipt format. start with Prefix, start with number or something you can think the most commonly used for your self. 6. add a configuration field for printing store's GST/HST input for tracking receipt's tax slip. In any printing field, place it within [Header & footer editor], just under the Header Area."
- Decisions taken with the user before starting (both affect existing financial data):
  - **Legacy `PaymentMethod.Card` displays as Debit Card.** The old label was literally the debit-card one, so Debit is what the shop was recording. The enum value is KEPT so un-re-saved rows still resolve a name everywhere.
  - **A store rate change re-prices editable orders only.** An order that can still be edited picks up the current store rate when opened; a read-only one (Completed/Shipped/Cancelled/Returned) keeps the rate it was actually charged, so a printed receipt can never disagree with the screen.
- Plan:
  - [ ] `PaymentMethod`: add DebitCard/CreditCard; keep Card as the legacy value.
  - [ ] Per-shop `PaymentTaxRules` (taxable + rate per method) and receipt-number format on `Shop`, with runtime column guards.
  - [ ] `PaymentTaxRuleService` + `OrderNumberFormatter`, bound to the active shop like `CurrencySettingService`.
  - [ ] New `ShopSettingsWindow` (payment/tax matrix + receipt format) off the Local Configuration menu.
  - [ ] `OrderEditWindow`: Debit/Credit radios everywhere, tax rate becomes a locked bold label driven by the rules.
  - [ ] GST/HST number in the header/footer editor, printed under the receipt header.
  - [ ] Localization keys in both blocks; build clean.
- Done:
  - [x] `Models/PaymentTaxRules.cs` (NEW): `PaymentTaxRule` (taxable + rate) keyed per method, `CreateDefault`
        (cash/e-transfer free, both cards 13%), `Normalize` (legacy `Card` → `DebitCard`), JSON round-trip, and a
        static `Active` the money calculation reads. **The type lives in Models, not Services, deliberately** —
        `Order.CalculateSectionPayment` cannot decide whether a portion is taxed without it, and a model reaching
        into a service is worse than a model owning the rule type.
  - [x] `Order.CalculateSectionPayment` gate changed from `method == Card` to
        `PaymentTaxRules.Active.IsTaxable(method)`. **The RATE still comes from the order** (what was charged and
        persisted), only the taxable/tax-free decision follows the shop — so no saved order re-prices behind the
        shop's back, while a method made tax free stops adding tax.
  - [x] `Shop`: `PaymentTaxRulesJson` + five receipt-numbering columns, with a new `ShopColumnMigrations` guard
        table in `App.xaml.cs`. **The CREATE TABLE guard was not enough**: an existing database already has Shops,
        so `IF NOT EXISTS` is a no-op there and every later column needs its own ALTER.
  - [x] `Services/OrderNumberFormatter.cs` (NEW): 4 modes (Timestamp = the legacy format and still the default,
        Sequential, DailySequential, YearlySequential) + prefix + padding. `Reserve` scans past numbers already
        taken; `CommitSequence` advances the counter **only after the order is saved**, so an abandoned form
        cannot burn a receipt number.
  - [x] `ShopSetupWindow` rebuilt as a scrolling card layout (identity / payment-tax matrix / receipt numbering /
        measurement terms) with a live number preview. The tax matrix is generated from
        `PaymentTaxRules.ConfigurableMethods`, so a method added to the enum later needs no XAML change.
  - [x] `OrderEditWindow`: Debit + Credit radios in all 6 groups; the three tax `TextBox`es became bold read-only
        value blocks (`LockedRateBox`/`LockedRateText` + a tooltip naming where the rate is set). The four
        radio helpers now take `PaymentSectionControls` instead of a positional radio list — with five deposit
        radios, a call site passing them in the wrong order would have compiled and silently read the wrong method.
  - [x] `ReceiptBrandingSettings.TaxRegistrationNumber` + a field directly under the Header card in the branding
        editor, mirrored across language tabs (one shared value, reentrancy-guarded). Printed under the header on
        the receipt, the measurement print, and the QuestPDF measurement export.
  - [x] 30 keys × 2 blocks; both blocks verified at **413 keys, identical sets, no duplicates**.
- **Verified by execution, not just by build** (scratch harness in the session scratchpad, referencing the built dll):
  - 35 assertions on numbering and tax rules, all passing: every format shape, blank prefix, padding clamping,
    daily/yearly rollover, commit advancing exactly one, a hand-typed number leaving the counter alone,
    yesterday's number not moving today's run, JSON round-trip, and the money split honouring the rules.
  - **One real bug found and fixed this way**: `ResolveNextSequence` compared the period key in *every* mode, so a
    continuous (Sequential) run carrying any stale key restarted at **1** and would have re-issued receipt numbers
    already handed to customers. Only period-based modes may roll over now.
  - Schema guards run against a **copy of the live database** with the DDL read out of the built assembly by
    reflection (so the test cannot drift from what ships): Shops went 8 → 14 columns, 6 statements applied, both
    existing shops survived reading back mode=0 / padding=4 / next=1 / rules=null (i.e. the documented defaults),
    16 orders untouched, and a second pass added nothing.
- Notes: build succeeded, **0 warnings / 0 errors**, and a full build with `SonarAnalyzer.CSharp` 10.30.0.144632
  temporarily referenced reported **zero Sxxxx findings** across the project (package then removed; `git diff` on
  the csproj is clean). Two justified `[SuppressMessage]`s were added in `ShopSetupWindow` for the documented WPF
  false positives — S2325 on a method that only reads `x:Name` radios, S1144 on a property consumed solely by
  `{Binding GroupName}`.
  - The live database is **deliberately not migrated yet**: startup blocks on the login window *before* the schema
    phase, so it will be migrated on the user's next sign-in. A backup was taken first at
    `%LOCALAPPDATA%\CameywareOrder\orders.db.bak-preShopRules`, and the live file was confirmed byte-size identical
    afterwards — launching to the login screen changed nothing.
  - Expect a CS0103 storm in the editor (SKILL §15): ~15 new `x:Name` controls plus two `.csproj` edits for the
    analyzer. `dotnet build` is clean, so it is the stale design-time model — restart the C# language server.

### 2026-07-27 15:25 — Rebrand LeeYongeOrdering → CameywareOrder  [DONE]
- Ask: "Now, Lets renaming the whole project, Lets call the program CameywareOrder, whenever you see LeeYongeOrder, rename to this. Why? Previously, we wanted to design a simple single store tracking system. Now the concept has been more scalable. now, we put everything managed/developed by Cameyware INC."
- Decisions taken with the user before starting (three non-mechanical calls inside an otherwise mechanical rename):
  - **Data folder renames WITH a one-time auto-migration.** `%LocalAppData%\LeeYongeOrdering`
    → `CameywareOrder`, moved on first launch when the new folder is absent and the old one
    exists. Without this, every existing installation would launch as a fresh install with an
    empty order list — the path is resolved independently in SIX places.
  - **`Main.HeaderTitle` (上海丽扬高级定制 / "Shanghai LeeYonge Bespoke") is NOT renamed.** It is
    the customer shop's own name — seeded into `Shop.NamesJson` and printed on receipts —
    whereas Cameyware INC is the vendor. Only product strings change.
  - **Repo directory keeps its name**; `.csproj`/`.sln`/assembly are renamed.
- Plan:
  - [ ] Rename namespace `LeeYongeOrdering` → `CameywareOrder` across all `.cs`/`.xaml`.
  - [ ] `git mv` the `.csproj`/`.sln`; assembly + root namespace follow the file name.
  - [ ] Surgical `Languages.xml` edit: product strings only, `Main.HeaderTitle` untouched.
  - [ ] New one-time LocalAppData folder migration, run before anything reads a path.
  - [ ] Docs: README + Architecture + the operational parts of context.md (process name and
        build command both change); historical TODO entries and verbatim asks left as written.
  - [ ] Build and confirm `Build succeeded. 0 Error(s)`.
- Done:
  - [x] 72 code/project files rewritten (`LeeYongeOrdering`→`CameywareOrder`,
        `LeeYonge Ordering`→`Cameyware Order`); `git mv` of the `.csproj`/`.sln`, both recorded as
        renames (R100/R092). Assembly and root namespace follow the project file name, so
        `CameywareOrder.exe` is produced and the kill-before-build process name changes with it.
  - [x] `Languages.xml`: 4 product strings changed, `Main.HeaderTitle` untouched; XML re-parsed at
        768 `<Text>` elements. Worth noting `App.MainTitle` ALREADY read "Cameyware订单录入系统" /
        "Order Entry System Designed by Cameyware" — the vendor/shop split was already in the data.
  - [x] NEW `Services/LocalDataFolderMigration.cs`, called as the FIRST statement of
        `StartApplicationAsync` — ahead of `EnsureDatabasePathReady()`, which creates the folder and
        would otherwise make the destination "already exist" and skip the move forever.
        Handles the empty-placeholder case (a renamed build launched once before the migration
        existed) and is deliberately FATAL on IOException/UnauthorizedAccess: continuing would
        show the shop an empty order list, which reads as data loss. The thrown message says the
        data is safe, names both folders, and points at the usual cause (another copy running).
  - [x] Stale old-named build outputs cleared (50 files) so nobody launches the wrong exe; a
        `LeeYongeOrdering.sln` stub the IDE regenerated on reload was deleted.
- **Verified against the live 76.8 MB data set, not just by build:**
  - Backup first: `%LocalAppData%\LeeYongeOrdering.FULLBACKUP-preRebrand`, confirmed identical at
    94 files / 76,834,895 bytes.
  - Launched the real exe (the migration runs before the login window, so this exercises the
    wiring too): legacy folder gone, `CameywareOrder` present, **94 files / 76,834,895 bytes —
    exact match**.
  - Launched again: legacy NOT recreated, data still 94 / 76,834,895. Idempotent.
  - Reflection-based testing was tried first and abandoned: Windows PowerShell 5.1 is .NET
    Framework and returns null for a type in a net8.0 assembly. Running the exe is the better
    test anyway — it covers the call site, which reflection would have skipped.
- Notes: build succeeded, 0 warnings / 0 errors. One failure en route, fixed: the new file used a
  bare `Path`, which is ambiguous under `ImplicitUsings` (`HotChocolate.Path` vs `System.IO.Path`)
  — the alias `using Path = System.IO.Path;` is required, exactly as `DocumentStorageService` does.
  Deliberately NOT renamed: the repo directory, `Main.HeaderTitle` (the customer shop's name), and
  the historical entries in this file including the verbatim `- Ask:` quote above.

### 2026-07-26 20:30 — Multi-shop + login (scalability)  [IN PROGRESS — Phase 0 of 6 DONE]
- Ask: "Lets scalable the Whole application" — add a login screen (roles Admin/Manager/Staff later, admin/admin for now, no complexity rules); after login the admin picks an existing shop or creates one; the current data becomes "Shanghai LeeYonge Bespoke"; a new shop collects name, preferred language, currency and measurement-terms setup; bilingual for now, multi-language later; role-gated behaviour left blank.
- Approved plan: `C:\Users\123\.claude\plans\crispy-wandering-moler.md`. Decisions taken with the user: ONE SHARED DATABASE with a `ShopId` column (not per-shop folders); orders always filtered to the open shop, no cross-shop view yet; switch shops via a Local Configuration menu item without re-login; global language for login, per-shop language once open; wizard seeds defaults or copies another shop; the standalone language picker is dropped so startup is login → shop picker; `credentials.json` holds a LIST of accounts from day one.
- A design review found five defects in the approved plan before any feature code was written. Folded in: the shop name must be LOCALIZED (`Main.HeaderTitle` is both 上海丽扬高级定制 and "Shanghai LeeYonge Bespoke" — a single string regresses the zh header and printed receipts); applying a shop's language would overwrite the global preference via the `LanguageChanged` handler; `ShopId` must be stamped centrally in `SaveChangesAsync` because `CopyOrderAsync` uses an explicit property list and would silently drop it; login/picker cancel would leave an invisible process under `OnExplicitShutdown`; and a zero-shops install needs its own path.

#### Phase 0 — foundation fix  [DONE]
- **Pre-existing, app-breaking bug found and fixed: a fresh install was broken outright.** `EnsureDatabaseCompatibilityAsync` ran BEFORE `MigrateAsync` and returned early when the `Orders` table did not exist, so on a machine with no `orders.db` all **38** `ALTER TABLE Orders ADD COLUMN` guards were skipped. The two migrations create only **15 + 3** columns against a model of ~50, so the first order query would throw "no such column". Never noticed because every dev machine already had a database — and multi-shop is exactly what would have hit it, since new shops mean new PCs.
- Done:
  - [x] `App.xaml.cs`: reordered the schema phase to **baseline → `MigrateAsync` → column guards**, with a load-bearing-order comment naming the failure it prevents. Verified there is NO overlap between `OrderColumnMigrations` and the three columns owned by `AddOrderPaymentFields`, so the reorder cannot cause a duplicate-column error.
  - [x] Renamed `EnsureDatabaseCompatibilityAsync` → `EnsureSchemaCompatibilityAsync`; its `HasOrdersTable` early return is now documented as a defensive no-op rather than the silent trapdoor it was.
  - [x] `OnStartup` body extracted to `StartApplicationAsync`, wrapped in try/catch → error dialog → `Shutdown(1)`. `OnStartup` can only be `async void`, so a throw past the first await previously made the app vanish with no message — and startup is about to gain a DB bootstrap.
  - [x] `<remarks>` on `OrderColumnMigrations`: **never run `dotnet ef migrations add`**. `AppDbContextModelSnapshot.cs` records 22 Order properties against the model's ~50, so a scaffolded migration would emit `AddColumn` for ~28 existing columns and fail with "duplicate column name" on every live installation. This — not merely house convention — is why new tables must use `CREATE TABLE IF NOT EXISTS` guards.
- Verified end to end, not just by build:
  - Full backup taken and file-count-verified at `%LOCALAPPDATA%\LeeYongeOrdering.FULLBACKUP-preMultiShop` (keep until multi-shop is finished).
  - Live `orders.db` moved aside, app launched against an empty folder: the new database contains every guard-only column (`LastModifiedDate`, `AlterationFinalTaxRate`, `StatusReasonCategory`, `ClothingFinalTaxRate`, `CustomMadeFinalTaxRate`, `AlterationBalanceCleared`, `CustomMadeRecordsJson`) as well as the migration-owned ones. Under the old ordering these were absent — that is the bug and its fix.
  - Live database restored and confirmed **byte-for-byte identical** to the pre-test backup.
  - Both migration ids are recorded in the live `__EFMigrationsHistory`, so `MigrateAsync` is a no-op there and the reorder provably changes nothing for existing data.
  - Technique worth reusing: SQLite stores each table's `CREATE TABLE` text (and row data) as UTF-8 inside the file and rewrites it on `ALTER TABLE ADD COLUMN`, so schema and migration history can be verified by reading the `.db` file directly — no sqlite3 CLI needed.
- Build: succeeded, 0 warnings / 0 errors.

#### Phase 1 — schema + shop bootstrap  [DONE]
- Done:
  - [x] `Models/Shop.cs`: `Id`, **`PublicId` (Guid)**, `Code`, **`NamesJson`** (+ `[NotMapped] Names`, `SetNames`, `ResolveName`), `PreferredLanguageCode`, `CurrencyType`, `CreatedAtUtc`, `IsArchived`.
    - `PublicId` exists because `Id` is a local autoincrement and whole databases are carried between machines (`GlobalSettingsPackage`, `DatabasePathProvider.ImportDatabaseFrom`). Anything stored OUTSIDE the database that belongs to a shop — its measurement-terms file, its branding folder — must key on `PublicId`, or an import silently hands one shop another's settings.
    - `NamesJson` (not a single `Name`) because the shop name is user-facing bilingual text printed on receipts; `Main.HeaderTitle` is both 上海丽扬高级定制 and "Shanghai LeeYonge Bespoke". Reuses the per-language dictionary pattern from `MeasurementTerm`/`GarmentType`.
  - [x] `Models/Order.cs`: `ShopId` with a comment forbidding hand-setting it (Phase 3 stamps it centrally).
  - [x] `Data/AppDbContext.cs`: `DbSet<Shop>`, Shop mapping, unique index on `PublicId`, `HasIndex(o => o.ShopId)`. `ShopId` is modelled as a **scalar only, no FK/navigation** — SQLite cannot add a foreign key to an existing table without rebuilding it.
  - [x] `App.xaml.cs`: `ShopId` entry in `OrderColumnMigrations` (`INTEGER NOT NULL DEFAULT 0`, so 0 is an unambiguous "unassigned" marker); new `EnsureShopSchemaAsync` (`CREATE TABLE IF NOT EXISTS Shops` + unique `IX_Shops_PublicId` + `IX_Orders_ShopId`) and `EnsureShopBootstrapAsync` (seed from `Main.HeaderTitle` in every installed language + current currency/language, then `UPDATE Orders SET ShopId = {0} WHERE ShopId = 0`, parameterised per S2077). Bootstrap returns immediately if any shop exists, so it cannot duplicate on relaunch.
- **GOTCHA that broke the first run — `ExecuteSqlRawAsync` treats its SQL as a composite format string.** A literal `DEFAULT '{}'` in the `CREATE TABLE` DDL was parsed as a malformed `{...}` placeholder and threw `FormatException: expected an ASCII digit` at offset 258 *before any statement ran*. Fixed by dropping the column default (the `Shop.NamesJson` property already defaults to an empty JSON object, so `NOT NULL` still holds) and commenting the trap. Swept every other `ExecuteSqlRaw` in the project — the only remaining brace is the intentional `{0}` parameter.
  - Two things this validated: the Phase 0 error handler turned a silent vanish into a precise message (type, text, offset) that located the bug in one pass; and the phased design contained it — the failure landed between two idempotent steps, so `ShopId` was already added, `Shops` was simply absent, no order was touched, and the retry continued from exactly where it stopped.
- Verified with a read-only SQLite inspector built in the scratchpad (`Mode=ReadOnly`; reusable for Phases 3 and 5):
  - before: 8 orders, 1 order item, no `Shops` table;
  - after: 8 orders, 1 order item **unchanged**; 1 shop, `PublicId` populated, names holding BOTH `zh-CN` and `en-US`, language `zh-CN`, currency CAD;
  - **0 orders unassigned, 0 orders pointing at a missing shop, all 8 claimed by shop 1.**
- Build: succeeded, 0 warnings / 0 errors.

#### Phase 2 — ShopContext + shop-scoped services  [DONE]
- Done:
  - [x] `Services/ShopContext.cs` (NEW): singleton, static `Instance` (bindable from XAML), `INotifyPropertyChanged`, `ShopChanged`. `RequireCurrent()` **throws** rather than returning a default — an order written against shop 0 disappears from every view with no error, so a loud failure at the point of the mistake is strictly better. `SetActive` assigns a **whole new object** to one field rather than mutating: GraphQL resolvers read this from Kestrel threads while the UI can switch shops, so a reader sees the old shop or the new one, never a torn mixture. `UpdateActiveShop(mutate)` persists through a scope factory supplied at startup.
  - [x] `CurrencySettingService`: `Current` now reads the bound shop's row; the JSON file is demoted to the pre-shop fallback and the seed the first shop was migrated from. `BindTo(shop)` + a shared `RaiseChanged()`; `SetCurrency` writes through `ShopContext.UpdateActiveShop`.
  - [x] `MeasurementTermsService`: per-shop file `measurement-terms-{PublicId:N}.json`; `BindTo(shop)` swaps the config **in place** (the `_config` field is readonly and callers hold references to its lists, so it follows the proven `ImportConfig` pattern).
  - [x] `ReceiptBrandingStore`: per-shop folder `Branding\{PublicId:N}\`. Stays static and stateless — it re-reads on every `Load()` — so it asks `ShopContext` for the shop each time rather than caching one.
  - [x] Shop name replaces `Main.HeaderTitle` in the `MainWindow` header (bound to `ShopContext.Instance.CurrentName` via a new `svc:` xmlns) and in `AddReceiptTitle`, so each branch prints under its own name. `CurrentName` falls back to `Main.HeaderTitle` when no shop is open, so neither can render blank.
  - [x] `_suppressGlobalLanguageSave` guard in `App.xaml.cs`: applying a shop's preferred language no longer rewrites `language-preference.json`, which backs the pre-shop screens.
- Two bugs caught in my own code before testing:
  - `BindTo` originally fell back to seeded defaults for a shop with no file yet — that would have silently replaced the user's customized measurement terms. Fixed with an explicit one-time adoption (`AdoptLegacyFileFor` / `AdoptLegacyFolderFor`) that COPIES the legacy files, leaving the originals as a rollback net. `BindTo` deliberately does NOT fall back to the legacy file, or every newly created branch would inherit the first shop's configuration.
  - The adoption sat inside the bootstrap's create-branch, so it would never have run for a database already bootstrapped by Phase 1. It now also runs as a catch-up for the **lowest-id** shop on every launch (both calls no-op once that shop has its own files).
- **Real-data finding that validated Phase 3's necessity**: after the test launches the inspector showed `Orders 8 -> 9` with the new order at `ShopId = 0`. An order created through the UI is not stamped, exactly as the design review predicted (`CopyOrderAsync` and the other creation paths build an Order from an explicit property list). Harmless while nothing filters, invisible the moment filtering lands. Response: the backfill was extracted to `ClaimUnassignedOrdersAsync` and now runs on **every launch**, not just at bootstrap — 0 can only mean "written before stamping existed", so the first shop is the only sensible owner, and it becomes a permanent no-op safety net once Phase 3 stamps every new order.
- Verified: per-shop `measurement-terms-6ad5a995….json` created and **hash-identical** to the legacy file (customizations intact); `Branding\6ad5a995…\receipt-branding.json` created (584 bytes, same as the original); legacy files left in place; still exactly **1 shop** after four launches, confirming bootstrap idempotency.
- Build: succeeded, 0 warnings / 0 errors.

#### Phase 3 — shop filtering on every read and write  [DONE]
- Done:
  - [x] `AppDbContext`: `_shopId` captured in the constructor as an **instance field** — EF parameterises instance-field references in a query filter, whereas a static lookup would be baked into the compiled query and only the first shop opened would ever work. Contexts are scoped and every operation creates a fresh scope, so a shop switch is picked up by the next query with nothing to invalidate. Zero (= no shop open, i.e. startup and design time) filters everything out, which fails safe: showing nothing is recoverable, showing another shop's orders is not.
  - [x] `HasQueryFilter(e => e.ShopId == _shopId)` on `Order` — one line that confines the list, search, printing and both GraphQL read resolvers, so future code cannot leak another shop's data by forgetting a `Where`. `IgnoreQueryFilters()` is the deliberate escape hatch for a cross-shop view later.
  - [x] `SaveChanges(bool)` / `SaveChangesAsync(bool, ct)` overridden (the no-arg overloads delegate to these, so the pair covers every save) calling `StampNewOrdersWithShop`: stamps `ShopId` **and** `CurrencyType` on every added order from `ShopContext.RequireCurrent()`, which throws when no shop is open. Central by design — `CopyOrderAsync` and the GraphQL create mutation build an Order from an explicit property list, and any one of them forgetting `ShopId` writes an order to shop 0 that saves without error and is then invisible everywhere.
  - [x] `Models/Order.cs`: `[GraphQLIgnore]` on `ShopId`, since `Query.GetOrders` carries `[UseFiltering]`/`[UseSorting]` and would otherwise publish `shopId` as a filterable field advertising other shops' existence.
- **Four cross-tenant holes closed in `GraphQL/Mutation.cs`.** `Find`/`FindAsync` are key lookups and **bypass EF query filters entirely**, so:
  - `UpdateOrderAsync` and `DeleteOrderAsync` could fetch and then mutate/delete another shop's order — both now use `FirstOrDefaultAsync(o => o.Id == …)` so the filter applies.
  - `AddOrderItemAsync` added the item to the change tracker BEFORE looking up the parent and saved regardless of whether it was found, so a line item could be attached to an order the caller could not see. The (filtered) parent lookup now comes first and throws when absent.
  - `RemoveOrderItemAsync` reached the item through `OrderItems.FindAsync`; `OrderItem` carries no shop of its own and Find bypasses filters anyway. It now comes in through the filtered `Orders` set, which also yields the order needed to recalculate the total in the same query.
  - Rule recorded at the top of the file: reach orders through a LINQ query, never `Find`.
- [x] `MainViewModel` hardening for the Phase 5 switch: `DeleteOrderAsync` no longer uses `FindAsync` (a stale selection after a switch could delete another shop's order), and `LoadOrdersAsync`'s catch path now clears `_allOrders` / `SelectedOrder` / paging — otherwise a failed reload leaves the PREVIOUS shop's orders on screen under the new shop's name, with `SelectedOrder` pointing at an order Delete/Copy/Print would act on.
- Verified against live data: orders went **9 → 12** during testing with **0 unassigned** and all 12 owned by shop 1. The Phase 2 failure (a UI-created order landing at `ShopId = 0`) is gone, the earlier orphan was claimed by `ClaimUnassignedOrdersAsync` on startup, and the list rendering correctly proves the query filter resolved the right shop — a wrong id would have shown an empty list.
- Build: succeeded, 0 warnings / 0 errors.

#### Phases 4-6 — remaining
1. ~~Foundation fix~~ **DONE**
2. ~~Schema + bootstrap~~ **DONE**
3. ~~ShopContext + shop-scoped services~~ **DONE**
4. ~~Shop filtering~~ **DONE**
3. `ShopContext` + shop-scoped `CurrencySettingService` / `MeasurementTermsService` / `ReceiptBrandingStore`, each with the `Reload()` they lack; localized shop-name resolver into the header and receipt title.
4. Filtering: EF global query filter + central `SaveChangesAsync` stamping, `MainViewModel`, Copy Order, and the six unguarded GraphQL primitives (`[GraphQLIgnore]` on `Order.ShopId`).
5. Auth: `UserRole`, `AuthenticationService` (PBKDF2, list of accounts), `LoginWindow` replacing the language picker; cancel paths must `Shutdown()`.
6. Shop UI: picker, new-shop wizard (creates the row BEFORE "Configure now" so the terms editor targets the new shop), Switch Shop menu item + in-place reload.
7. Localization keys, orphan sweep for the removed `LanguageSelection.*`, docs.

## Completed

### 2026-07-27 14:10 — Run both quality gates; clear every SonarQube finding  [DONE]
- Ask: "skill :wpf-skill. run the IDE checking and SONAQUBE FIXES"
- **Neither gate's tooling exists in this session** — no `get_errors`/diagnostics tool and no
  SonarLint/SonarQube tool in the registry (the IDE diagnostics seen earlier arrive unprompted from
  a PostToolUse hook on `Edit`, which cannot be invoked on demand). Reproduced both from the CLI:
  - **Gate 1** — `dotnet build` for real compiler diagnostics: 0 errors, 0 warnings.
  - **Gate 2** — added `SonarAnalyzer.CSharp` 10.30.0 as a PackageReference, built, read the
    `warning Sxxxx` lines, then **removed the package** (`git diff` on the csproj confirms it is
    back to its committed state). SonarLint IS this analyzer, so the rules and ids match the IDE.
    **Technique worth reusing whenever SonarLint is unavailable.**
- Scope note: the session's own changes (`App.xaml.cs`, `MainWindow.xaml`) came back **completely
  clean**; `MainWindow.xaml` has no C# to analyse. All 11 findings were in untouched files, fixed
  because the ask was explicitly for the fixes.
- Fixed (11 findings / 6 files), re-analysed to zero:
  - [x] **S3267** `AppDbContext.StampNewOrdersWithShop` — projected with `.Select(entry => entry.Entity)`
        and iterated orders directly instead of reaching through `entry.Entity` twice per iteration.
  - [x] **S6444 ×7** (`OrderEditWindow` 3, `CustomMadeServiceWindow` 4) — every `Regex` now carries a
        1-second match timeout. A `RegexTimeout` field was added to each file **above** the patterns:
        static field initializers run in TEXTUAL order, so declaring it below would hand the
        constructors `TimeSpan.Zero`, which `Regex` rejects — and it would surface as a
        `TypeInitializationException` on first keystroke, not as a build error.
  - [x] **S2325** `MeasurementTermsWindow.ShowDuplicateTermWarning` → `static`. Checked before
        complying: it touches no `x:Name` control and no field, only the localization singleton, so
        this is a REAL finding rather than the documented WPF false positive.
  - [x] **S125** `MainWindow.xaml.cs` — a prose comment flagged as commented-out code. Exactly the
        pattern context.md already warns about (semicolon + parenthetical reading as syntax);
        reworded into plain sentences, no behaviour touched.
  - [x] **S1144** `ShopPickerWindow.ShopRow.Name` — **false positive, suppressed with justification.**
        It is consumed by `{Binding Name}` in the picker item template (`ShopPickerWindow.xaml:52`);
        XAML bindings are invisible to Roslyn. Deleting it would have blanked the shop name.
- Notes: build succeeded, 0 warnings / 0 errors, both with the analyzer present and after removing
  it. No DB, XAML or string-table change. `Details`/`Shop` on the same `ShopRow` are equally
  XAML-only but were NOT flagged — Sonar's inconsistency, per SKILL §10; left untouched deliberately.

### 2026-07-27 12:45 — Startup died with "Failed to bind 127.0.0.1:5050 — address in use"  [DONE]
- Ask: "When i tried to login to read records, it says System.IO.Exceptions: Failed to bind 127.0.0.1:5050... address in use. can you find how we can avoid this"
- Diagnosed live before changing anything: `Get-NetTCPConnection -LocalPort 5050` named PID 15892 —
  an already-running `LeeYongeOrdering` with an EMPTY `MainWindowTitle`, holding the port and the
  database. So the trigger is a second copy of the app, often one left running with no window.
- **Root cause was the blast radius, not the port.** `web.UseUrls(...:5050)` bound a fixed port and
  `await _host.StartAsync()` was unguarded, so a bind failure propagated to `OnStartup`'s catch →
  message box → `Shutdown(1)`. Confirmed by grep that NOTHING in the app consumes the endpoint (no
  `HttpClient`, no reference to the URL outside `App.xaml.cs`) — the UI reads and writes through
  `AppDbContext` directly. An external integration surface was taking down the order book with it.
- Done, all in `App.xaml.cs`:
  - [x] `ServerPort` const split into `PreferredServerPort` (5050) + `AnyFreePort` (0). New
        `ResolveServerPort()` runs BEFORE the host is built — the URL is baked in at build time, so
        retrying another port after a failed start would mean rebuilding the whole container.
  - [x] Fallback resolves a **concrete** free port via `TryFindFreePort()` rather than handing
        Kestrel `localhost:0`. With port 0 Kestrel resolves the one hostname to two loopback
        addresses and takes a SEPARATE ephemeral port for each; picking the number first makes the
        fallback bind exactly like the 5050 path, with one address to report.
  - [x] `StartApiServerAsync()` wraps `StartAsync` and catches **`IOException` only** — that is what
        Kestrel wraps a bind failure in. A broader catch would swallow a genuinely broken hosted
        service and start the app in a state nobody checked. On failure the app runs with no API.
  - [x] `internal static string? ApiEndpoint` records the address actually bound, read back from
        `IServerAddressesFeature` (with `AnyFreePort` the real port is only known after the bind).
        Logged at Information, or a warning naming the degradation.
- Verified by execution, not just by build: a scratch console app running the identical
  resolve/start/read-back path with 5050 deliberately occupied chose **57132**, bound it, and read
  the address back — startup completed. Re-run with 5050 free chose **5050**. Both paths proven.
- Notes: build succeeded, 0 warnings / 0 errors. No DB, XAML or string-table change. The transient
  Sonar S1144 "unused private field" flags on the two new consts cleared once the helpers landed.
  Deliberately NOT added: a single-instance mutex. It would also stop the port collision, but with
  one shared database and in-app shop switching, two instances is plausibly something the user
  wants; refusing the second launch is a bigger behaviour change than moving a port nobody reads.

### 2026-07-27 11:36 — Left-align the Custom Service column content  [DONE]
- Ask: "go over the project, and do a minor update on 定制服务 on the main application, make the content left aligned."
- Done:
  - [x] `MainWindow.xaml`, the `Order.Fields.CustomMadeFlag` column cell template: the `CustomMade.Flag.Yes`/`.No` flag `TextBlock` and the bracketed garment-names `TextBlock` both moved from `HorizontalAlignment="Center" TextAlignment="Center"` to `Left`.
  - [x] The wrapping `StackPanel` went `HorizontalAlignment="Center"` → `"Stretch"`. Needed, not incidental: a centered panel measures to its content, so left-aligning the children inside it would left-align them against the *text block*, not the column, and the whole group would still sit centered in the cell. Stretch makes the panel fill the column width, which also keeps `TextWrapping="Wrap"` breaking the garment names at the full 170px column rather than at the widest line.
- Notes: presentation-only — no converter, model, schema or string-table change; the column header stays as it was. Build succeeded, 0 warnings / 0 errors.

### 2026-07-26 20:00 — Payment breakdown: add a pre-tax final-balance row to both stages  [DONE]
- Ask: "Improve the payment section's breakdown — 添加一个税前尾款，在定金付款stage，放置在税前小计下；在尾款支付的stage，新添加一个税前尾款，放置于此服务总计税之上，税前定金之下。"
- Value shown in both places is `SectionPayment.FinalBase` (pre-tax subtotal minus the pre-tax deposit), so the row states what is still owed before any card tax.
- Done:
  - [x] New key `Order.Fields.PreTaxFinalBalance` (税前尾款 / "Pre-Tax Final Balance") in both language blocks. Distinct from `Order.Fields.FinalBalance` (剩余尾款, the taxed amount still outstanding) and `Order.Fields.PreTaxDownpayment`.
  - [x] Deposit-stage panels (`*DepositBreakdownPanel`, all 3 sections): grew from 3 to 4 rows; the new row sits at index 1, directly under `PreTaxSubtotal`, pushing `ServiceStageTax` to 2 and `PostTaxTotal` to 3. New value blocks `Alteration/CustomMade/ClothingPreTaxBalanceText`.
  - [x] Final-stage panels (`*FinalBreakdownPanel`, all 3 sections): grew from 6 to 7 rows; the new row sits at index 2, between `PreTaxDownpayment` (1) and `ServiceTotalTax` (3), pushing the per-portion tax-split `StackPanel` to 4, `PostTaxTotal` to 5 and `FinalBalance` to 6. New value blocks `Alteration/CustomMade/ClothingFinalPreTaxBalanceText`.
  - [x] All six populated from `money.FinalBase` in the three `Refresh*Totals` methods.
- Verified: build succeeded 0 warnings / 0 errors; 6 new `x:Name`s declared, 6 assignments in code-behind, 6 `PreTaxFinalBalance` label bindings; both language blocks at 342 keys with identical sets. The new `x:Name`s produced the usual `CS0103` stale-design-time-model false positives in the editor (SKILL §15), cleared by the build with no code changed for them.
- Notes: presentation only — no model, schema or calculation change. Row renumbering was done as six explicit whole-block edits rather than a scripted find/replace, because a mis-numbered `Grid.Row` fails silently (WPF clamps out-of-range rows) instead of breaking the build.

### 2026-07-26 19:45 — "None" as alteration default, shared money-input behaviour, deposit ceiling  [DONE]
- Ask: "1. 修改衣服\"无\"为默认选项。2. 所有Input价格为0时，再次修改时要避免出现012这种类型的价格。可以参考税前定金的input定义。3. 在当前服务付款中，当税前定金价格高于税前服务总价时应该阻止用户继续输入，并且弹窗告知用户，用户确认后，税前定金应该等于税前服务总价。"
- Done (1) — "None" is the alteration default:
  - [x] Moved the `Tag="None"` item to FIRST in `AlterationCategoryBox`, so the existing `SelectedIndex = 0` on the new-order path makes it the default (matches the first-option convention in SKILL §5).
  - [x] **Legacy guard**: the edit-load fallback no longer uses `SelectedIndex = 0`. An unmatched stored category (free text from before the dropdown existed, or null) now selects `DefaultSavedAlterationCategoryTag` = `GarmentAdjustments` via the new `SelectAlterationCategory(tag)`. Falling back to index 0 would have switched a charged legacy alteration service OFF and dropped it from the totals.
- Done (2) — one money-input behaviour everywhere:
  - [x] `RegisterDepositBox` generalised to `RegisterMoneyBox` (decimal/paste filtering + zero-clearing focus + restore-zero-on-blur) and applied to the alteration price, all three tax boxes, all three deposit boxes, and the runtime-created clothing unit/promotional price boxes. Handlers renamed `OnMoneyBoxGotFocus`/`OnMoneyBoxLostFocus`.
  - [x] `OnMoneyBoxGotFocus` now also skips **read-only** boxes, not just disabled ones: a read-only box still takes focus, and clearing its text programmatically succeeds — it would have blanked e.g. a tax box that is 0 because the stage is settled by cash.
  - [x] `restoreZeroOnBlur: false` for two boxes where BLANK carries its own meaning: the **promotional price** (blank = no promotion) and the **alteration price** (blank = the service is absent, per `HasItems`; forcing "0" would silently enrol it as an unpriced service and trigger the unpriced warnings).
  - [x] `OnMoneyBoxLostFocus` refresh downgraded to `runAutoComplete: false` — restoring a zero must not move a payment-method selection, and the deposit boxes' own TextChanged already ran the auto-complete pass.
- Done (3) — deposit ceiling:
  - [x] New `PaymentSectionControls.SectionSubtotal` (pre-tax, the actual ceiling — `SectionTotal` is post-tax and would allow a deposit above the pre-tax price).
  - [x] `EnforceDepositCeiling(box)` called from `OnDownpaymentAmountChanged`: warns via `OrderEdit.Warn.DepositExceedsTotal` (naming the service and the capped amount), then pins the box to the subtotal and puts the caret at the end. Skipped while the section is unpriced (nothing to cap against).
  - [x] New `_enforcingDepositCeiling` guard — the modal warning pumps messages and the correction raises TextChanged again, so without it the dialog stacks.
  - Note: `CalculateSectionPayment` already clamped the deposit silently; this makes the clamp visible so a typo cannot hide behind numbers that quietly stop responding.
- Notes: build succeeded 0 warnings / 0 errors; both language blocks verified at 341 keys with identical sets. No DB/schema change.

### 2026-07-26 19:15 — Pick-up confirmation for unpriced services + "None" alteration category  [DONE]
- Ask: "1. 在edit order panel，当点击已取货时，如果价格可能有误，提醒一下用户，告诉他哪些service没有被charge（价格没有或者为0），让用户确认，并且提醒用户，一旦确认取货，此订单不再更改。2. 修改衣服可以添加一个新的option, \"无\"，用来指示没有修改衣服的服务，且锁定附加说明和收款项目。其最终并不参与计算。"
- Done (1) — pick-up confirmation:
  - [x] Extracted `UnpricedServiceList()` from `WarnAboutUnpricedServices` so the clear-all warning and the new pick-up confirm share one definition of "has items but no charge".
  - [x] New `ConfirmPickUp()` returns false to cancel; `OnPickedUpChanged` reverts the tick inside the `_syncingStatus` guard when the user declines, so the handler is not re-entered. A fully priced order is not interrupted at all.
  - [x] `OrderEdit.Confirm.PickUpUnpriced` names the unpriced services AND states that confirming marks the order completed and no longer modifiable (both parts of the ask in one dialog).
- Done (2) — "None" alteration category:
  - [x] `Views/OrderEditWindow.xaml`: third `ComboBoxItem` with `Tag="None"` on `AlterationCategoryBox`, listed **last** so the existing first-option default (`GarmentAdjustments`) is unchanged. New key `Alteration.Category.None`.
  - [x] `PaymentSectionControls.ServiceSwitchedOff` (optional `Func<bool>`) + `IsServiceSwitchedOff`; only Alterations supplies it, via the new `AlterationServiceSwitchedOff` property (`ServiceDetails` tag == `NoAlterationServiceTag`).
  - [x] Excluded from calculation: `HasItems()` returns false when switched off, and `RefreshAlterationTotals` uses a price of 0 — the price box VALUE is kept, so switching the category back restores it.
  - [x] Locked: `ApplySectionInputLocks` and `ApplySectionLock` both fold in `IsServiceSwitchedOff` (price, tax, deposit box, all method radios, both checkboxes), and `RefreshPricingLocks` marks `AlterationAdditionalNotesBox` read-only. `AlterationCategoryBox` itself deliberately stays enabled — it is the only way back out of "None".
  - [x] `OnServiceCategoryChanged` upgraded from `RefreshServicesTotalBreakdown()` to `RefreshComputedTotals(runAutoComplete: false)`: the category now affects totals and locks, not just the breakdown text.
- Round-trip: `ServiceDetails` stores `"None"` like any other category, the edit ctor's tag-matching loop reselects it, and `AlterationAddedToReceipt` stays false (total 0) so the section is absent from the receipt, the detail panel and the order-total breakdown.
- Notes: build succeeded 0 warnings / 0 errors; both language blocks verified at 340 keys with identical sets. No DB/schema change. Reported to the user: the read-only warning appears only when a service is unpriced (the literal reading of the ask) — offered to show it on every pick-up instead.

### 2026-07-26 18:45 — Project README  [DONE]
- Ask: "Add a Readme markdown file for this project"
- Done: new `README.md` at the repository root — what the app is, the three service sections,
  requirements and build/run (including the kill-before-build note for the self-locking exe),
  a table of every `%LOCALAPPDATA%\LeeYongeOrdering\` storage path (all verified against the
  source), backup/migration via the global-settings package, project layout, architecture
  notes (runtime column guards, the single `CalculateSectionPayment` money path, per-portion
  tax, derived status state, the embedded GraphQL endpoint), the localization workflow, and a
  contributing section pointing at the `AgentSkills/wpf-dev` companion files.
- Notes: docs only, no build impact. Written in English per the skill's persona rule; the one
  menu path a reader needs to follow is given in both languages since the UI is bilingual.

### 2026-07-26 18:30 — One-click global settings export/import + Import/Export menu reorder  [DONE]
- Ask: "添加导入导出新功能 — 一键导出全局设置，包括货币，量身，数据库.....等等；添加导入一件恢复所有本地设置功能。UI navigation reordering: 在本地配置中的导入导出 submenu reorder 成为 -> 添加或更改页眉页脚....量身项目设置....本地数据库....全局设置"
- Done:
  - [x] `Services/GlobalSettingsPackage.cs` (NEW, static): one zip holding `settings.json` (currency, language code, `MeasurementTermsConfig`, `BrandingExport`, version + timestamp) plus a nested `database.zip`. `ExportTo` / `TryRead` / `Import`.
    - The database package is **nested rather than re-implemented** — `DatabasePathProvider.ExportDatabaseTo` is written to a temp file and embedded, so the db + WAL/SHM sidecars + the whole `Documents/` tree keep one code path, and restore reuses `ImportDatabaseFrom` with its existing auto-backup and zip-slip guarding.
    - `TryRead` validates without side effects (returns null for anything unreadable, catching InvalidData/Json/IO/UnauthorizedAccess/NotSupported) so the destructive confirm is only offered for a real package.
    - `Import` applies only the sections the package actually carries, so an older or partial package never blanks out settings it knows nothing about. Database first — it is the only destructive step and the only one that takes its own backup. An unknown language code is skipped rather than failing the whole restore.
  - [x] `Services/ReceiptBrandingStore.cs`: extracted `BuildExport()` (returns the `BrandingExport` object); `ExportConfigJson()` now just serializes it. The package embeds the object directly instead of nesting a JSON string inside its own JSON.
  - [x] `MainWindow.xaml.cs`: `OnExportGlobalSettingsClick` / `OnImportGlobalSettingsClick`, following the existing dialog + confirm + status-bar pattern; reloads the order grid after a restore. `DescribePackageContents` lists only the parts present in the file, so the confirmation never promises to restore something the package lacks.
  - [x] `MainWindow.xaml`: Import/Export submenu reordered to HeaderFooter → MeasurementTerms → LocalDatabase, then a `Separator` and the new `Toolbar.GlobalSettings` entry (Import/Export pair), matching the requested order.
  - [x] `Languages.xml` (zh-CN + en-US): `Toolbar.GlobalSettings`, `ImportExport.GlobalSettingsConfirm`, `Status.ExportGlobalSettings{Succeeded,Failed}`, `Status.ImportGlobalSettings{Succeeded,Failed}`, `Status.ImportGlobalSettingsInvalid`.
- Notes: build succeeded 0 warnings / 0 errors; both language blocks verified at 338 keys with identical sets. No DB/schema change. Verified by build and code review only — the export/import round-trip has not been exercised interactively, so it needs a real round-trip test (export, change a few settings, import, confirm everything comes back). As with the standalone database import, a restart is still the safest way to guarantee every open view reflects the swapped data.

### 2026-07-26 18:00 — Bug: final-balance method stays on Card after switching the deposit to Cash  [DONE]
- Ask (follow-up with concrete figures): with a custom-made pre-tax service total of 1234 and the deposit method set to Cash — deposit 234 gave tax 130 / total 1364, deposit 0 gave tax 160.42 / total 1394.42. Cash should not be taxed at all.
- Diagnosis, confirmed arithmetically against those figures: 130 = (1234−234) × 13% and 160.42 = 1234 × 13%. Both are the FINAL portion taxed at the card rate while the deposit portion is untaxed — i.e. `depositRate = 0` (Cash, correct) but `finalRate = 13` (Card, wrong). The final-balance method was stuck on Card.
- Root cause, and it was self-inflicted by the 14:30 change: `ApplyPaymentFields` persists the final method through `EffectiveFinalMethod`, which resolves to the deposit's method when the user has not picked one. That made persisted values match the display (correct, SKILL §4) but destroyed the distinction between *inherited* and *deliberately chosen*. On reload an inherited "Card" is indistinguishable from an explicit one, so `EffectiveFinalMethod` stopped substituting and the balance kept the card rate no matter what the deposit was switched to.
- Fix — keep the inheritance live instead of only resolving it at read time:
  - [x] `PaymentSectionControls.FinalMethodUserChosen` — set to true only when the user clicks one of the section's own final-method radios.
  - [x] `OnPaymentOptionChanged` rewritten around a new `FindSectionForRadio` + `IsFinalMethodRadio` pair: a final radio sets the flag; a deposit radio routes to the new `ApplyDepositMethodChange`, which keeps the existing reset behaviour (clear deposit-received; zero the amount only for "None") and additionally **re-mirrors the final method onto the new deposit method whenever the flag is false**. "None" mirrors to null, since there is no method to inherit.
  - [x] `LoadPaymentFields` seeds the flag via `InferFinalMethodWasChosen`: a stored final method that DIFFERS from the deposit's must have been deliberate, so it is protected; an equal one is treated as inherited (re-mirroring an equal value is a no-op until the deposit actually changes). This preserves a genuine "deposit by card, balance by cash" override across save/reopen.
  - [x] `TryGetDownMethodResetTargets` deleted — `FindSectionForRadio` supersedes it. New `AllSections` property reused by `WarnAboutUnpricedServices`.
- Explained to the user, NOT a defect: once both portions share a method AND a rate, the section's total tax is mathematically invariant to the deposit split (`deposit×r + (subtotal−deposit)×r ≡ subtotal×r`), so the three rows of the deposit-stage panel legitimately do not move when the deposit changes. They only move when the two portions differ in method or in rate. Offered to surface the per-portion split in the deposit-stage panel if that ambiguity is unwelcome.
- Notes: build succeeded 0 warnings / 0 errors. Verified by arithmetic against the reported figures rather than interactively.

### 2026-07-26 17:30 — Bug: a section with no charge becomes uneditable after save/reopen  [DONE]
- Ask: "有个bug。当我选择定制服务时，此时我选择了银行卡，但是我没有选择定金（计算价格应该为0），我没有按定金已收，此时我保存了。下一次我准备去修改，此时我打开了编辑订单，选择了定制服务，但是当我再去切换支付方式的时候，定金支付方式下的breakdown不再更改了。似乎被锁住了一样"
- Two independent defects, both triggered by a section that has items but **no charge**:
  - **Defect 1 — the settlement lock fires on a section with nothing to settle.** `IsSectionCleared` treats a zero-total section as cleared (nothing is owed), so `BalanceClearedCheck` ends up ticked. `ApplySectionLock` and `ApplySectionInputLocks` then read that tick alone as "settled" and disable the four deposit-method radios, the deposit box, the tax box and the item editors. The section stops responding to payment-method clicks and the deposit breakdown never recomputes — exactly the reported "似乎被锁住了一样". It is also self-perpetuating: the price inputs are frozen at zero, so the section can never be given a charge that would un-clear it.
    - Fix: new `IsSettled(c)` = `BalanceClearedCheck.IsChecked is true && SectionTotal() > 0m`. Replaced every "cleared tick alone" test with it — both lock methods, the two section-level blocks in `RefreshPricingLocks`, `RefreshCustomMadeButtonLabel`, and the record-open gate in `OnEditCustomMadeRecordClick`.
  - **Defect 2 — save silently discarded the section's payment state.** `ApplyPaymentFields` gated persistence on `_xxxSumTotal > 0m`, so a section with items but no charge fell into the `else` branch and `ClearSectionPaymentFields` nulled its downpayment method, deposit-received flag and cleared flag. On reopen `LoadPaymentFields` restored a null method, leaving all four deposit radios unchecked, and `UpdateSectionVisibility` collapses `PricingPanel` when no deposit radio is selected — so the deposit box, the breakdown and the whole final block disappeared.
    - Fix: the three gates now use `HasItems()`, the same test the breakdown and the clear-all pass already use. A section with no items at all is still cleared out, so an untouched section's default Cash selection never reaches the database.
- Side effect worth noting: this also repairs the zero-priced round-trip flagged in the 16:30 entry — `AlterationSubtotal` is now persisted as `0` instead of `null`, so the price box reloads as "0" and `HasItems()` stays true across save/reopen. `Order.IsBalanceCleared` still early-returns on `TotalAmount <= 0m`, so an all-zero order continues to read Outstanding in the list; that part is unchanged.
- Notes: build succeeded 0 warnings / 0 errors. Diagnosed by tracing the lock and persistence paths, not by reproducing interactively — asked the user to confirm against their exact steps.

### 2026-07-26 17:00 — String-table audit: collapse the duplicate tax label, prune orphaned keys  [DONE]
- Ask: "now we have <Text key=\"Order.Fields.DepositTax\">Current Tax</Text> — where is this used for? It seems have duplicated areas for it. checking where is it used, removing / refactoring if the same logic used. Do we have other similar fields properties not participate in calculation? Find and fix. Or it does participate in logic, but I think Current Tax context is not so clear. then update."
- Finding: `Order.Fields.DepositTax` and `Order.Fields.ServiceTotalTax` labelled **the same computed value**. Both rows display `money.Tax` (the section's whole tax) — `DepositTax` in each section's deposit-stage panel, `ServiceTotalTax` in each final-stage panel. The panels are mutually exclusive, so the same figure was simply called two different things depending on which stage was showing. `DepositTax` was also the vaguer of the two ("Current Tax" says nothing about scope).
- Done:
  - [x] `Views/OrderEditWindow.xaml`: all 3 deposit-stage tax rows repointed to `Order.Fields.ServiceTotalTax`; `Order.Fields.DepositTax` deleted. One key, one meaning, six bindings.
  - [x] Swept the whole string table for orphans: extracted every `<Text key>` and grepped the source for each, then excluded the families built by interpolation (`Measure.Term.{id}`, `Measure.Garment.{id}`, `ClothingItem.{key}`, `PaymentMethod.{m}`, `AgeType.{t}`, `CurrencyType.{c}`, `ReturnReason.{c}`, `ServiceType.{t}`, `Alteration.Category.{t}`, `OrderEdit.Panel.{enum}` — verified the last can only ever produce `CustomFromScratch`/`MeasurementsOnly`, never `Currency`). **23 keys × 2 blocks = 46 entries removed**: `Order.Fields.{DepositTax, CurrentTaxRate, PrepaidDownpayment, SumTotal, TotalPrice, ChestSize, JacketLength, DownpaymentMethod, FinalBalanceMethod}`, `OrderEdit.Panel.Currency`, `OrderEdit.PrintMeasurements`, `Receipt.PrintedAt`, `LanguageSelection.Confirm`, `CustomMade.Documents.{Empty, Upload}`, `TermLanguage.{Language, Name}`, `Measure.Section.{Jacket, Shirt}`, `Measure.{Chest, Length, SitAround, Sleeves}`.
    - `Order.Fields.CurrentTaxRate` was orphaned earlier this session by the stage-aware tax label; the rest are residue from the currency-goes-global, Measurement-Terms and receipt-wording changes.
  - [x] Verified after the sweep: XML parses, both blocks hold **330 keys, identical sets, no duplicates**, and a re-run of the orphan sweep reports nothing left.
- Model-level findings (reported, NOT changed — these are DB-backed and deleting them is a migration decision for the user):
  - `Order.ChestSize` / `Order.JacketLength` — written as `null` on every save and never read; fully superseded by the Measurement Terms system. Only surviving use is a field copy in `MainViewModel.CopyOrderAsync`.
  - `Order.CurrencyType` — already documented as retained-but-unused; the receipt reads the global `CurrencySettingService`, not this column.
  - `Order.Subtotal` / `TaxRate` / `Downpayment` / `DownpaymentMethod` / `FinalBalanceMethod` — legacy aggregates that DO still participate, as fallbacks for pre-per-section orders (`AlterationTaxRate ?? TaxRate`). Keep.
- Notes: build succeeded 0 warnings / 0 errors. Skill-side changes from the same request are logged in `SkillUpdates.md`, not here.

### 2026-07-26 16:30 — Order.Fields.AllServicesTotalAmount: confirmed-receipts only, item-driven clear-all, aligned breakdown  [DONE]
- Ask: "1. 实收定金和实收尾款一定要等相应的checkbox点击完毕之后才能执行计算。2. 当结清所有尾款的时候，对每个服务都要执行定金已收和尾款结清（没有 order item 不参与运算；有 item 但没选支付方式则默认现金；有 item 但价格为 0 仍参与运算，标注价格有误并提示用户）。3. 以上 order item 规则也要体现在 breakdown。4. breakdown 左对齐，label 与 全部服务总金额 同列，价钱在价钱列。"
- Done:
  - [x] **(1)** `Models/Order.cs` `ReceivedDownpayment` now sums through `SectionReceivedDeposit(money, XxxDownpaymentCompleted)` — a deposit only counts once its checkbox is ticked. `OrderEditWindow.RefreshPaymentSummary` mirrors it via its own `SectionReceivedDeposit(money, controls)`. 实收尾款 was already gated on `BalanceClearedCheck`, left as-is. Changed the MODEL too, not just the editor, so the saved order reports the same figure (SKILL §4).
  - [x] **(2)** `PaymentSectionControls` gained `HasItems` / `SectionTotal` / `ServiceNameKey` (Funcs + key set in `InitializePaymentSectionControls`) and a derived `HasMissingPrice`. "Has items" = custom-made records exist / clothing rows exist / **for Alterations, a non-empty price box** (it has no item list of its own — assumption stated to the user). `ApplyClearAllToSection` rewritten to take the control group: skips item-less sections, defaults a null deposit method to Cash, ticks **`DownCompletedCheck` as well as** `BalanceClearedCheck`, and treats an explicit "None" deposit as "nothing to confirm" (final method falls back to Cash). `WarnAboutUnpricedServices` shows one non-blocking `MessageBox` listing services with items but no charge.
  - [x] **(3)** `AddServiceTotalDetail` now gates on `HasItems()` instead of `total > 0`, so a zero-priced service is listed rather than dropped — flagged with `Order.Fields.ServiceTotalUnpriced` (（价格有误）) and drawn in amber (`UnpricedLineBrush`).
  - [x] **(4)** Summary `Grid` got `Grid.IsSharedSizeScope="True"` and its label column `SharedSizeGroup="SummaryLabel"`; the breakdown panel now spans columns 0–1 and each line is built by `BuildBreakdownRow` as its own 2-column `Grid` joining that shared-size group. Labels line up under Order.Fields.AllServicesTotalAmount, amounts under its figure.
  - [x] Knock-on fix: `IsOrderBalanceCleared` gated on `_totalAmount <= 0m`, so an order made only of zero-priced items could never be cleared — `RefreshPaymentSummary`'s `ClearAllBalancesCheck.IsChecked = cleared` would have sprung the tick straight back off, defeating rule 2's third bullet. Now gated on "no section has items".
  - [x] `Languages.xml` (zh-CN + en-US): `Order.Fields.ServiceTotalUnpriced`, `OrderEdit.Warn.UnpricedServices`; `Order.Fields.ServiceTotalLine`/`LineNoDetail` reshaped to label-only ({2} amount placeholder dropped) now that the amount is its own column.
- Notes: build succeeded 0 warnings / 0 errors. Deliberate decisions reported to the user: (a) unticking OrderEdit.ClearAllBalances clears the balance ticks but leaves 已收定金 set — a received deposit is a fact, and `ClearAllBalancesCheck` is also driven by derived state, so reverting it would silently wipe manual ticks; (b) **known limitation** — `Order.IsBalanceCleared` still gates on `TotalAmount <= 0m` and `ApplyPaymentFields` persists a zero-total section as absent, so an order whose services are ALL zero-priced still reads Outstanding once saved. Fixing that reaches into `XxxAddedToReceipt` and the printed receipt, well beyond this ask.

### 2026-07-26 16:00 — Small-print service breakdown under Order.Fields.AllServicesTotalAmount  [DONE]
- Ask: "在订单中，全部服务总金额下方用小字给出breakdown，比如 修改衣服（服装修改）：$123 / 定制服务 (衬衫、西装)：$1234"
- Done:
  - [x] `Views/OrderEditWindow.xaml`: summary card gained a row — `ServicesTotalBreakdownPanel` sits in column 1 directly under `TotalAmountText`; the 实收定金 / 剩余尾款 / Order.Fields.BalanceStatus rows and `FinalBalanceBreakdownPanel` shifted down one (rows 2/3/4).
  - [x] `Views/OrderEditWindow.xaml.cs`: `RefreshServicesTotalBreakdown()` (called from `RefreshAllServicesTotalAmount`, right after the total) emits one small grey line per section whose total > 0, via `AddServiceTotalDetail`. Parenthetical detail per section: Alterations → its localized service category (`AlterationDetailText`), CustomMade → the measured garment names (`CustomMadeDetailText`), ReadyMade → the distinct item categories actually priced (`ClothingDetailText`). Shared `ListSeparator` (zh 、 / en ", ") matches `CustomMadeServiceFlagConverter`; `LocalizeWithFallback` keeps legacy free-text category values readable.
  - [x] `Services/CustomMadeMeasurementReader`: new `GetGarmentNames(IEnumerable<CustomMadeServiceRecord>, languageCode)` overload; the existing `Order` overload delegates to it. The editor holds unsaved records rather than an `Order`, and this avoids a second copy of the garment-name extraction.
  - [x] **Refresh gap closed**: neither `AlterationCategoryBox` nor the clothing rows' `categoryBox` had any change handler, so both parentheticals would have gone stale on edit. Added `OnServiceCategoryChanged` (XAML `SelectionChanged`) and a `categoryBox.SelectionChanged` subscription in `AddClothingItemRow`; both call `RefreshServicesTotalBreakdown()` only — a category carries no money, so a full `RefreshComputedTotals` would be wrong-altitude.
  - [x] `Languages.xml` (zh-CN + en-US): `Order.Fields.ServiceTotalLine` (`·  {0}（{1}）：{2}` / `·  {0} ({1}): {2}`) and `Order.Fields.ServiceTotalLineNoDetail`. The **whole line shape** lives in the string table because the parentheses and colon are fullwidth in Chinese and ASCII in English — concatenating them in C# would have produced `Alterations（Garment Adjustments）：$123` in English.
- Notes: build succeeded 0 warnings / 0 errors. Presentation only — no model, schema or calculation change; the lines are the same three section totals the grand total is summed from. GOTCHA recorded: a new brush had to be `System.Windows.Media`-qualified (ImplicitUsings makes bare `Color`/`SolidColorBrush` ambiguous against QuestPDF/HotChocolate) and is created once via `CreateFrozenBrush` rather than per line.

### 2026-07-26 15:40 — Final breakdown: per-portion tax split under Order.Fields.ServiceTotalTax  [DONE]
- Ask: "Ok, in the final breakdown on the calculation in 尾款支付方式 > breakdown 此服务总计税 having little fonts under it. -定金（现金）税收： $0 / -尾款（银行卡）税收：$XXX"
- Done:
  - [x] `Views/OrderEditWindow.xaml`: new `TaxBreakdownLine` style (FontSize 11, #7A8698, wrapping). All 3 `*FinalBreakdownPanel` grids gained a 6th row: a `StackPanel` in column 1 directly under the Order.Fields.ServiceTotalTax amount holding `*DepositTaxLineText` + `*FinalTaxLineText`; 税后总价 and 剩余尾款 shifted to rows 4/5.
  - [x] `Views/OrderEditWindow.xaml.cs`: `PaymentSectionControls` gained `DepositTaxLine`/`FinalTaxLine`; new `UpdateTaxBreakdownLines(c, money)` (called from all 3 `Refresh*Totals` right after the total-tax line) fills them from `money.ReceivedDownpayment - money.Deposit` and `money.FinalCharge - money.FinalBase`; `PaymentMethodName` resolves the method label (null → `PaymentMethod.None`). The final line uses `EffectiveFinalMethod`, so the named method always matches the one the tax was actually computed with.
  - [x] `Languages.xml` (zh-CN + en-US): `Order.Fields.DepositTaxLine` (定金（{0}）税收：{1} / "Deposit ({0}) tax: {1}") and `Order.Fields.FinalTaxLine` (尾款（{0}）税收：{1} / "Final balance ({0}) tax: {1}"). Both formatted via `LocalizationService.Format`, so the separator/punctuation stays per-language.
- Notes: build succeeded 0 warnings / 0 errors. Presentation only — no model, schema or calculation change; the two lines always sum to the Order.Fields.ServiceTotalTax figure above them. Applied to all 3 sections, not just Alterations, since they share the panel shape.

### 2026-07-26 15:15 — Per-stage tax rate: one shared tax-rate box that follows the deposit / final stage  [DONE]
- Ask: "we had Order.Fields.DepositTax, now it is labeling as Current Tax, what we want is to have this become a shared input field for both deposit and final balance staging payment. For instance, when I'm in deposit stage and change to card, I can modify the current tax rate to 5%. But when I am in Final balance stage, I can change the tax rate to 7% on the card. So this field shouldn't always be bound on the deposit stage, it is used for calculation based on which stage you are currently in."
- Clarified via question: (1) ONE shared box that swaps by stage, not two side-by-side boxes; (2) entering the final stage carries over the deposit's rate (falling back to the standard 13% when the deposit portion wasn't card).
- Done:
  - [x] `Models/Order.cs`: new `AlterationFinalTaxRate` / `ClothingFinalTaxRate` / `CustomMadeFinalTaxRate` (`decimal?`). The existing `XxxTaxRate` columns are now the DEPOSIT-stage rate. `CalculateSectionPayment` takes `depositRatePercent` + `finalRatePercent` (6 params, still under S107); each section's `XxxMoney` passes `XxxFinalTaxRate ?? XxxTaxRate ?? 0m` so **legacy orders with a single stored rate compute exactly as before**.
  - [x] `App.xaml.cs`: 3 runtime column guards for the new columns.
  - [x] `Views/OrderEditWindow.xaml.cs`: `PaymentSectionControls` now carries `DepositTaxRate` / `FinalTaxRate` / `ShowingFinalRate` / `IsFinalStage` (`DownNone` checked OR deposit marked received — "None" means no deposit was taken, so the balance IS the charge). New `ApplyStageTaxRates(c)` banks the typed value against the stage the box was showing, resolves both rates through `ResolveStageRate` (non-card → 0; card with no rate → deposit falls back to 13, final falls back to the deposit's rate), then rewrites the box **only** on a stage flip or a forced change — so a half-typed "5." is never normalised out from under the caret. Replaces the old `ApplyTaxRateRule`; `CardUsed` became dead and was removed.
  - [x] Lock rule (`ApplySectionInputLocks`): the tax box now keys off the CURRENT stage's method and `sectionLocked` rather than `inputsLocked` — marking the deposit received is precisely what hands the box over to the final rate, so it must become editable again there (the price/deposit boxes still lock as before).
  - [x] Stage-aware label: the 3 tax labels got `x:Name`s (`AlterationTaxLabel` etc.), text set by `UpdateTaxLabel`; new keys `Order.Fields.DepositTaxRate` (定金税率 (%) / Deposit Tax Rate (%)) and `Order.Fields.FinalTaxRate` (尾款税率 (%) / Final Balance Tax Rate (%)) in both blocks. `Order.Fields.CurrentTaxRate` is now unused by this window but left in place.
  - [x] Load/save: new `LoadStageTaxRates`/`LoadSectionTaxRates` seed both rates and point the box at the loaded order's actual stage — called AFTER `LoadPaymentFields`, because with no payment radio selected yet the card/cash rule would zero a stored rate before it was ever used (the old code had this same latent flaw, which would have reset a saved 5% to 13% on reopen). `ApplyPaymentFields` persists both rates from the section state (not from the box, which only holds one stage); the zero-total `else` branches null both. `GetTaxRateForServiceType` (legacy `Orders.TaxRate`) now returns the deposit rate, matching how the model reads it back.
- Notes: build succeeded 0 warnings / 0 errors. The new `x:Name`s produced the expected `CS0103` stale-design-time-model false positives in the editor (SKILL §15) — cleared by the build, no code changed for them. SonarLint MCP not available this session, so Gate 2 was not run.

### 2026-07-26 14:30 — 修改衣服付款: section totals don't reflect the pre-tax price / tax rate  [DONE]
- Ask: "The project has onging bugs for 修改衣服付款 component: keep the current logic for calculation for taxes and balances, however the data automation didn't get refreshed correctly. Rules for input areas — rule 1: empty input counts as 0; rule 2: any change on the input fields should run automation on prices; rule 3: only currency digit format accepted. TODO Bug 1: in alteration service, after entering the pre-tax service price and switching to Card, the tax rate shows 13% (right) but with 124 entered the breakdown shows 税前小计 124 / 当前计税 0 / 税后总价 124 — it should be 124, (124-0)*0.13, 124+(124-0)*0.13. Solution: the change of pre-tax service total price should bind into the calculation of the after-tax total."
- Root cause (NOT a missing refresh — the TextChanged automation does fire): `Order.CalculateSectionPayment` taxes each portion only when **that portion's** method is Card. Selecting Card for the **deposit** makes `PaymentSectionControls.CardUsed` true, so `ApplyTaxRateRule` displays 13% — but the **final** method is still null, so `finalRate = 0` and the whole 124 final base goes untaxed. The tax box therefore advertises a rate that the totals never apply.
- Done:
  - [x] `Views/OrderEditWindow.xaml.cs`: new `static EffectiveFinalMethod(PaymentSectionControls)` — the final balance inherits the deposit's method until the user explicitly picks one (`None` never inherits; an explicit selection always wins). Same convention `AutoCompleteSection` / `ApplyClearAllToSection` already use. Wired into all 3 `Refresh*Totals` **and** `ApplyPaymentFields`, so the saved order recomputes to exactly the amounts the editor displayed (SKILL §4).
  - [x] The current-tax row now shows the section's whole tax (`money.Tax`) in all 3 sections instead of only the deposit's tax (`ReceivedDownpayment - Deposit`), so it pairs with the post-tax-total line beneath it. `Languages.xml` en value `Order.Fields.DepositTax` "Tax on Deposit" → "Current Tax" (zh 当前计税 already correct; value change, not a new key).
  - [x] Rule 2 gap: clothing item rows (add button, unit-price/promo-price TextChanged, remove button) called only `RefreshClothingTotals()`, so editing a ready-made line item left the order grand total + payment summary stale. All 4 now call `RefreshComputedTotals(runAutoComplete: false)` — full refresh, but never moves a payment-method selection (same rule as the price/tax boxes).
  - [x] Rules 1 & 3 verified as already holding: `ParseDecimalOrZero` maps empty/invalid → 0, and `DecimalInputPattern` `^\d*(\.\d{0,2})?$` is on `PreviewTextInput` + the paste handler for every money box incl. the dynamically-created clothing rows.
- Traced result for the reported case (124, Card deposit, no deposit entered): 税前小计 $124.00 / 当前计税 $16.12 / 税后总价 $140.12.
- Notes: build succeeded 0 warnings / 0 errors. The tax/balance calculation engine (`Order.CalculateSectionPayment`) is untouched per the ask — only *which* final method is fed into it. No DB/schema change; legacy orders keep their stored null final method and are unaffected. The final-method **label** was deliberately left on the raw selection: `FinalBlock` is only visible once the deposit is marked received, at which point `AutoCompleteSection` has already set the radio explicitly — so the label never contradicts the money. SonarQube/SonarLint MCP tooling was not available in this session, so Gate 2 was not run; the new code follows the §9a rules by construction (static helper, no `== true`, no nested ternary, 2 branches).

### 2026-07-26 14:00 — Doc maintenance: bring Architecture.md back in sync with the code  [DONE]
- Ask: "Analyze the architecture.md file under the agent skills, and then checking what does this project do" → then "yes go ahead" (update the drifted docs).
- Findings: `Architecture.md` had fallen behind several sessions of work — components that only ever got recorded in `context.md`/`TODO.md` were never promoted into the component map, and one cross-cutting rule had become factually wrong.
- Done (`AgentSkills/wpf-dev/Architecture.md`):
  - [x] **Services/**: added `CurrencySettingService`, `ReceiptBrandingStore` (+ `LogoPlacement`/`BrandingExport`), `BrandingRenderer`; expanded `MeasurementTermsService` with `MeasurementGender`, `EnableCustomMeasurements`/`RestoreDefaultMeasurements`, and the export/import trio.
  - [x] **Data/**: documented `DatabasePathProvider.ExportDatabaseTo`/`ImportDatabaseFrom` (zip package incl. the `Documents/` tree, zip-slip guard, legacy raw-`.db` fallback).
  - [x] **Models/**: `Order.StatusReasonCategory` + `StatusReason` pair, `IsPickedUp` = Shipped or Completed; `MeasurementTerm.Gender` + `GarmentType.UseCustomMeasurements`.
  - [x] **Converters/**: added the 4 undocumented ones — `BalanceStatusColorConverter`, `OrderServicesSummaryConverter`, `ReturnReasonSummaryConverter`, `DocumentThumbnailConverter` (and named `LastItemBorderThicknessConverter`).
  - [x] **Views/**: added `ReceiptBrandingWindow` and `CurrencySettingWindow`.
  - [x] **Migrations/**: was "InitialCreate + snapshot"; there are two (`AddOrderPaymentFields`), with later columns arriving via the runtime guards.
  - [x] **MainWindow**: documented the Local Configuration menu incl. the Import/Export submenu (3 export/import pairs, confirm dialogs, dated file names).
  - [x] **Cross-cutting**: corrected the refunded-receipt rule (it no longer omits `Order.Fields.FinalBalance` — only the `Order.Fields.PaymentBreakdown` breakdown is swapped for the reason); recorded that read-only status = Completed/Shipped/Cancelled/Returned across the three predicates that must change together; added the self-contained Import/Export rule.
- Done (`AgentSkills/wpf-dev/context.md`): workspace path corrected `c:\` → `d:\Projects\LeeYongeOrdering`.
- Notes: docs only — no source files touched, so no build/Sonar run applies.

### 2026-07-26 13:30 — Real fix: red strikethrough still spanned full row (previous fix was ineffective)  [DONE]
- Ask: "之前提的 checkbox strok 横贯整个 row 的问题依旧没有修改。我觉得在 checkbox 外增加一层 block 正好长度跟它一样，这样对这个层直接 covered by red cross line。当然要有相应的逻辑去控制什么时候添加这个带 crossline 的层。应该可以解决。"
- Real root cause (the previous session's `HorizontalAlignment="Left"` fix on the checkboxes did NOT work): the `NotApplicableCheckBox` style's `ControlTemplate` drew the strike with a `Line Stretch="Fill"`. A `Stretch="Fill"` shape measures itself against whatever available width its parent offers (not the sibling content's actual size), so it always inflated to roughly the row width during Measure — `HorizontalAlignment="Left"` on the checkbox only affects Arrange positioning, not that inflated Measure-time DesiredSize, so it had no visible effect.
- Done (implements the user's own suggested design — a same-sized wrapper layer with a controlled strike overlay):
  - [x] `Views/OrderEditWindow.xaml`: `NotApplicableCheckBox` ControlTemplate no longer draws the `Line` at all (keeps only the red box + strikethrough label). New `NotApplicableCheckBoxStrike` `Border` style (Height 1.5, red background, `HorizontalAlignment="Left"`, `Visibility="Collapsed"` by default). All 8 refund-lock checkboxes (`ClearAllBalancesCheck`, `PickedUpCheck`, and the Alterations/CustomMade/Clothing `*DownCompletedCheck`/`*BalanceClearedCheck` pairs) are now each wrapped in a `Grid` alongside a sibling `Border` using that style, with `Width="{Binding ActualWidth, ElementName=<checkbox>}"` — an explicit numeric Width binding is always respected regardless of available space, unlike `Stretch="Fill"`.
  - [x] `Views/OrderEditWindow.xaml.cs` `ApplyNotApplicableCheckboxStyle`: now toggles each strike Border's `Visibility` alongside the existing `Style` swap, via a new `SetNotApplicableCheckbox(checkbox, strike, style, visibility)` helper (keeps the method's cognitive complexity low, matches the existing `SetPaymentSectionEnabled`-style helper convention).
- Notes: build succeeded 0 warnings/errors; SonarQube clean. Recorded the underlying `Stretch="Fill"`-in-template gotcha in `/memories/repo/startup.md` to avoid repeating it.

### 2026-07-26 13:00 — UI fix: red strikethrough on payment checkboxes spanned the full row  [DONE]
- Ask: "查看订单或者修改订单 > 订单在已取消/已退货的情况下我们有 red stroke 会划掉 checkbox。但是修改衣服付款的component里: checkbox 应该是 inline block 的，此 stroke 横贯了整个 row。看一下什么原因。只需要横贯当前所占的checkbox长度就行了。"
- Root cause: the 6 payment "已收定金"/"OrderEdit.BalanceCleared" checkboxes (Alterations/CustomMade/Clothing × deposit-completed + balance-cleared) sit directly inside **vertical** `StackPanel`s, where children default to `HorizontalAlignment="Stretch"` (fill the row width) — unlike the working `OrderEdit.PickedUp`/`OrderEdit.ClearAllBalances` checkboxes, which sit in a **horizontal** `StackPanel` (children auto-sized, not stretched). The shared `NotApplicableCheckBox` style's strikethrough `Line` (`Stretch="Fill"`) then filled the checkbox's full stretched render width instead of just its content.
- Done:
  - [x] `Views/OrderEditWindow.xaml`: added `HorizontalAlignment="Left"` to `AlterationDownCompletedCheck`, `AlterationBalanceClearedCheck`, `CustomMadeDownCompletedCheck`, `CustomMadeBalanceClearedCheck`, `ClothingDownCompletedCheck`, `ClothingBalanceClearedCheck` — content-sized ("inline-block") like the other checkboxes, so the red strikethrough now only spans the checkbox + label.
- Notes: build succeeded 0 warnings/errors. XAML-only fix; no code-behind/Sonar-relevant changes (style itself unchanged).

### 2026-07-26 12:30 — Bug fix: Shipped orders are now read-only (view-only)  [DONE]
- Ask: "bug fix: If the order is shipped, the order record is considered as completed, shouldnot be modifed anymore but can view."
- Done:
  - [x] `MainWindow.xaml.cs` `IsReadOnlyStatus` (drives the toolbar/context-menu Edit/View label): added `OrderStatus.Shipped` alongside Completed/Cancelled/Returned.
  - [x] `Views/OrderEditWindow.xaml.cs` `IsReadOnlyStatus` (drives `_isReadOnly` → `ApplyReadOnlyMode()`): same addition, so opening a Shipped order now locks every field/control and hides Save, same as Completed/Cancelled/Returned.
  - [x] `ViewModels/MainViewModel.cs` `IsClosedStatus` (used by Copy Order to reset the new copy's status back to Processing): added `OrderStatus.Shipped` too — otherwise copying a Shipped order would have produced an immediately-locked duplicate, a new bug flowing directly from the same change.
- Notes: build succeeded 0 warnings/errors; SonarQube clean on all 3 changed files. No DB/schema change — purely status-classification logic. `Order.IsPickedUp` already treated Shipped same as Completed for list styling/balance semantics, so this aligns the edit-lock with that existing convention.

### 2026-07-26 12:00 — Preset return/cancel reason picker + detail-panel/receipt payment-breakdown bug fix  [DONE]
- Ask: "1. 订单页面中,退货理由的退货理由必须要有,可以提供几个common的选项 (客户不想要/服务不满意/购买的产品有问题/价格太贵/其他;选择其他时需要给出理由,保存订单时此理由不能为空,设置default). bug修复: 已退货/已取消时 UI 收款明细仍显示,应改为显示退货/取消理由;打印PDF除已有内容外,所有UI页面内容都要展现出来。"
- Clarified via question: for the printed receipt of a cancelled/returned order, show full parity with a normal receipt (item names + prices + totals); only the payment-method breakdown line is replaced by the reason (not a wholesale "hide all charges" receipt).
- Done:
  - [x] `Models/Order.cs`: new `StatusReasonCategory` (nullable string key) alongside the existing `StatusReason` (now only used as the "Other" free-text detail).
  - [x] `App.xaml.cs`: `ALTER TABLE Orders ADD COLUMN StatusReasonCategory TEXT NULL;` guard.
  - [x] `Converters/ReturnReasonSummaryConverter.cs` (NEW): `IMultiValueConverter` + public static `Resolve(category, freeText)` — non-Other category → localized `ReturnReason.{category}` label; Other/blank → the free text (or "-").
  - [x] `Views/OrderEditWindow.xaml`: row 4 replaced the freetext-only box with `StatusReasonCategoryBox` (5 presets: CustomerDoesNotWant/ServiceUnsatisfactory/ProductIssue/PriceTooHigh/Other); new row 5 holds the existing freetext `StatusReasonBox`+placeholder-hint, now gated on category=="Other" (not just refunded status).
  - [x] `Views/OrderEditWindow.xaml.cs`: `UpdateStatusReasonVisibility` defaults the category to index 0 when first shown (per the "always pre-select first option" convention); new `UpdateOtherReasonRowVisibility`/`OnStatusReasonCategoryChanged`/`LoadStatusReasonCategory` (legacy fallback to "Other" for pre-existing records saved before this picker existed); `ValidateStatusReason` (category required, freetext required when Other) wired into `TryValidateForSave`; `ApplyStatusReasonFields` persists category+freetext together (both cleared when status isn't Cancelled/Returned); `ApplyReadOnlyMode` disables the new combo too.
  - [x] `MainWindow.xaml` (detail panel bug fix): wrapped the `Order.Fields.PaymentBreakdown` label+value in a `StackPanel` that collapses via `DataTrigger` when `SelectedOrder.IsRefunded`; added a new label+value block (MultiBinding → `ReturnReasonSummaryConverter`) shown only when `IsRefunded`, in the same slot.
  - [x] `MainWindow.xaml.cs` (`AddReceiptTotals`): full charge/payment breakdown (item sections, totals, downpayment, final balance) now always renders regardless of refund status (reverted the earlier "hide everything" approach); only the payment-method-breakdown paragraph is swapped for a Order.Fields.CancelReason/Order.Fields.ReturnReason section (via `ReturnReasonSummaryConverter.Resolve`) when `order.IsRefunded`.
  - [x] `Languages.xml` (zh-CN + en-US): `ReturnReason.CustomerDoesNotWant/ServiceUnsatisfactory/ProductIssue/PriceTooHigh/Other`, `OrderEdit.Validate.StatusReasonRequired`, `OrderEdit.Validate.StatusReasonOtherRequired`.
- Notes: build succeeded 0 warnings/errors; SonarQube clean on all changed files. No further DB migration beyond the new column. Receipt printing and the PDF share the exact same `BuildReceiptDocument`/`AddReceiptTotals` code path, so the fix covers both automatically.

### 2026-07-26 11:30 — Receipt: hide charge breakdown for cancelled/returned orders, show reason instead  [DONE]
- Ask: "小票页面内容优化 1. 如果已退货或者取消，那么不需要展示收费明细，但是需要标出退货或者取消原因。->这应该也要在打印PDF中体现出来"
- Done:
  - [x] `MainWindow.xaml.cs` `BuildReceiptDocument`: when `order.IsRefunded`, skips `AddAlterationReceiptSection`/`AddClothingReceiptSection`/`AddCustomMadeReceiptSection`/`AddReceiptTotals` (the whole charge/payment breakdown) and calls new `AddRefundedReceiptSummary` instead — shows only the coloured Order.Fields.BalanceStatus line + a Order.Fields.CancelReason/Order.Fields.ReturnReason section (label picked by `order.Status`) with `order.StatusReason` (falls back to "-" when blank), then Notes if present. Removed the now-dead `!order.IsRefunded` guard inside `AddReceiptTotals` (that method is only ever called for non-refunded orders now).
  - [x] `Languages.xml` (zh-CN + en-US): `Order.Fields.CancelReason` (Order.Fields.CancelReason/Cancellation Reason), `Order.Fields.ReturnReason` (Order.Fields.ReturnReason/Return Reason).
- Notes: build succeeded 0 warnings/errors; SonarQube clean on `MainWindow.xaml.cs`. Receipt printing (`OnPrintReceiptClick`) and the "print receipt + measurements" flow both reuse `BuildReceiptDocument`, and printing to PDF goes through the same `PrintDialog`/FlowDocument path (via a PDF printer driver) — so this single change covers both the on-screen print and the PDF output, per the ask. No DB/schema change (reuses `StatusReason` added in the previous session).

### 2026-07-26 11:00 — Cancel/return reason box, address-required-on-Shipped validation, basic-info beautify  [DONE]
- Ask: "TODO: 如果订单状态为已取消或者已退货, 1. 在订单界面地址栏下方生成一个textbox写退货理由 如果取消那就变成取消理由，里面有placeholder让用户输入退货/取消理由 2. 如果状态改为已发货，更改/保存时地址一栏不能为空，要有validation 3. 优化编辑菜单的整体页面，现在inputbox, radio button还有textbox太单调。美化页面同时增加一些icon让页面更加美感。"
- Done:
  - [x] `Models/Order.cs`: new nullable `StatusReason` string property (backs both `Order.Fields.CancelReason`/`.ReturnReason`, same field).
  - [x] `App.xaml.cs`: `ALTER TABLE Orders ADD COLUMN StatusReason TEXT NULL;` runtime guard.
  - [x] `Views/OrderEditWindow.xaml`: wrapped the basic-info fields (order#, status, name, phone, email, address) in a `SectionCard` with a "基本信息"/"Basic Information" heading; added a `FieldIcon`+`FieldLabel` style pair and a Segoe MDL2 Assets glyph before each label (Tag E8EC, Flag E7C1, Contact E77B, Phone E717, Mail E715, MapPin E707 — verified against Microsoft's official icon list, see repo memory). Added a new row 4: `StatusReasonLabelPanel` + `StatusReasonContainer` (TextBox + placeholder-hint TextBlock overlay, same pattern as `MeasurementTermsWindow`'s search box), collapsed by default.
  - [x] `Views/OrderEditWindow.xaml.cs`: `UpdateStatusReasonVisibility()` shows/hides the row and swaps the placeholder between `OrderEdit.Placeholder.CancelReason`/`ReturnReason` based on selected status; called from both constructors, `OnStatusChanged`, and `RefreshLocalizedLabels` (language switch). `OnStatusReasonTextChanged` toggles the hint like the measurement-terms search box. `TryValidateForSave` now rejects Save when status is Shipped and Address is blank (warning dialog + focus, mirrors the Phone/Email required checks). `ApplyEditableFields` persists `StatusReason`; `ApplyReadOnlyMode` marks the box read-only for finalized orders.
  - [x] `Languages.xml` (zh-CN + en-US): `Order.Fields.StatusReason`, `OrderEdit.Placeholder.CancelReason`/`ReturnReason`, `OrderEdit.Validate.AddressRequired`, `OrderEdit.Panel.BasicInfo`.
- Notes: build succeeded 0 warnings/errors; SonarQube clean on `OrderEditWindow.xaml.cs`, `Order.cs`, `App.xaml.cs`. Icon codepoints verified via https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-fluent-icons-font (Segoe Fluent Icons is the renamed superset of Segoe MDL2 Assets — same codepoints for this range) instead of guessing. Only the basic-info section was restyled per the ask ("整体页面" scoped to the plain inputbox/label area — the payment/service panels below were already beautified in an earlier session).

### 2026-07-26 10:00 — Dated export file names (archiving)  [DONE]
- Ask: "导出的文件名称需要自动加上日期作为结尾，方便做归档" (exported file names should automatically append a date suffix, for easier archiving)
- Done:
  - [x] `MainWindow.xaml.cs`: new `BuildDatedExportFileName(baseName, extension)` helper (`"{baseName}-{yyyyMMdd}.{extension}"`); applied to all 3 export `SaveFileDialog` defaults — `measurement-terms-<date>.json`, `orders-backup-<date>.zip`, `header-footer-branding-<date>.json`.
- Notes: build succeeded 0 warnings / 0 errors; SonarQube clean on `MainWindow.xaml.cs`. Date-only suffix (no time) per the request's wording; user can still rename in the save dialog.

### 2026-07-26 09:30 — Audit: media assets in Import/Export must migrate cleanly  [DONE]
- Ask: "Analyze all Import/export feature. Make sure any records or configs which has media resources related and have Import/export, the media assets(images), should be base64 saving to DB or if you have already implemented differently, its fine. The purpose is that we could migrate the whole application easily to another PC. Please verify if it follows the same business rules."
- Findings: 3 Import/Export features exist (Measurement Terms JSON, Local Database, Header & Footer JSON). Measurement terms has no media. Branding logo was already self-contained (base64 in JSON, from the prior session). **Gap found:** custom-made document images (`CustomMadeDocument`/`DocumentStorageService`) live as files under `LocalAppData/LeeYongeOrdering/Documents/CustomMade`, referenced only by `StoredFileName` inside `Order.CustomMadeRecordsJson` — the DB export/import only copied `orders.db` (+wal/shm), so migrating the DB to another PC left every attached image reference dangling.
- Done:
  - [x] `Data/DatabasePathProvider.cs`: `ExportDatabaseTo` now writes a zip package (`orders.db` + wal/shm sidecars + the entire `Documents/` folder tree) instead of a raw `.db` copy. `ImportDatabaseFrom` tries the zip package first (validates it contains an `orders.db` entry, extracts with zip-slip path-containment guarding via `ExtractPackageSafely`), and falls back to the legacy raw-`.db`-copy path (catches `InvalidDataException` from `ZipFile.OpenRead`) for backward compatibility with previously-exported plain `.db` files. Both the current db AND the current `Documents/` folder are backed up (`orders.db.bak-<ts>`, `Documents.bak-<ts>`) before being overwritten.
  - [x] `MainWindow.xaml.cs`: export/import dialogs now default to `.zip` (`orders-backup.zip`, filter "Backup Package (*.zip)"), import dialog also still accepts legacy `.db`/`*.*`.
  - [x] `Languages.xml` (zh-CN + en-US): reworded `ImportExport.DatabaseConfirm` to mention attached images are included/backed up too.
- Notes: build succeeded 0 warnings / 0 errors; SonarQube clean on `DatabasePathProvider.cs` and `MainWindow.xaml.cs`. No DB schema change (images stay file-based, not blobbed into SQLite — bundling via zip achieves the same "self-contained migration" goal without an invasive schema/perf tradeoff). See `/memories/repo/startup.md` for the durable rule going forward: any future Import/Export of a record/config with attached media must keep the export self-contained (base64-in-JSON for small single assets like the logo, or a bundled zip package for larger/many files like custom-made document images).

### 2026-07-26 09:00 — Import/Export for Header & Footer (header/footer branding)  [DONE]
- Ask: "agent skill: wpf-dev, Previous features: Add a new tab on navigation called Import/Export under the local configuration... TODO: Add Import/export for 添加或更改页眉页脚. Make 页眉页脚 -> 导入导出"
- Done:
  - [x] `Services/ReceiptBrandingStore.cs`: `ExportConfigJson()` / `TryParseConfigJson(json)` / `ImportConfig(export)` + new `BrandingExport` DTO — self-contained JSON export includes the settings (header/footer XAML per language, logo placement) plus the logo image as base64, so import restores the logo file too (writes to `logo.<ext>`, clears any stale logo file first).
  - [x] `MainWindow.xaml`: added a third nested submenu under `Toolbar.ImportExport`, reusing `Toolbar.HeaderFooter` as the label, with Export/Import entries (mirrors Measurement Terms/Local Database).
  - [x] `MainWindow.xaml.cs`: `OnExportBrandingClick`/`OnImportBrandingClick` — SaveFileDialog/OpenFileDialog, invalid-JSON warning dialog, Yes/No overwrite confirmation before import (mirrors measurement-terms handlers), status bar reporting.
  - [x] `Languages.xml` (zh-CN + en-US): `Status.Export/ImportBrandingSucceeded/Failed`, `Status.ImportBrandingInvalid`, `ImportExport.BrandingConfirm`.
- Notes: build succeeded 0 warnings / 0 errors; SonarQube clean on both changed files. No DB/schema change.

### 2026-07-25 — Import/Export menu (Measurement Terms JSON + local database)  [DONE]
- Ask: "Add a new tab on navigation called Import/Export under the local configuration. hover on the option, we have 量身项目设置, hover on it, it expand two options Export or Import. Clicking on Export, it will export 量身项目设置 as json, while I import, it can be recover the configuration from it. we have 本地数据库, also have export and Import feature. Analyze the whole project and implement both features."
- Done:
  - [x] `MainWindow.xaml`: new top-level `Local Configuration` submenu **Import/Export** (`Toolbar.ImportExport`), containing two nested menus mirroring the existing entries — **Measurement Terms** (reuses `Toolbar.MeasurementTerms`) → Export/Import, and **Local Database** (reuses `Toolbar.LocalDatabase`) → Export/Import.
  - [x] `Services/MeasurementTermsService.cs`: `ExportConfigJson()` (indented JSON of the current config), `TryParseConfigJson(json)` (static, returns null on invalid/corrupt JSON instead of throwing), `ImportConfig(config)` (replaces `Terms`/`Garments`, re-runs `MergePredefined` so an export from an older app version still gets today's predefined ids/gender classifications, then persists + raises `ConfigChanged`).
  - [x] `Data/DatabasePathProvider.cs`: `ExportDatabaseTo(targetPath)` and `ImportDatabaseFrom(sourcePath)` (both call `SqliteConnection.ClearAllPools()` first to release any pooled file handles before copying). Import auto-backs-up the current `orders.db` to `orders.db.bak-<timestamp>` alongside it before overwriting, and syncs the `-wal`/`-shm` sidecar files (deletes stale ones, copies matching ones from the source).
  - [x] `MainWindow.xaml.cs`: 4 new handlers (`OnExportMeasurementTermsClick`/`OnImportMeasurementTermsClick`/`OnExportDatabaseClick`/`OnImportDatabaseClick`) using `Microsoft.Win32.SaveFileDialog`/`OpenFileDialog` (matching the existing `CustomMadeServiceWindow`/`ReceiptBrandingWindow` pattern). Both imports show an explicit Yes/No confirmation (`MessageBoxImage.Warning`) before overwriting, since both are destructive; DB import also reloads the order grid (`_viewModel.LoadOrdersCommand.Execute(null)`) afterward. All failures/successes report through the existing `_viewModel.StatusMessage` status-bar pattern.
  - [x] Languages.xml (zh-CN + en-US): `Toolbar.ImportExport/Export/Import`, `Status.Export/ImportMeasurementTerms{Succeeded,Failed}`, `Status.ImportMeasurementTermsInvalid`, `Status.Export/ImportDatabase{Succeeded,Failed}`, `ImportExport.MeasurementTermsConfirm`, `ImportExport.DatabaseConfirm`.
- Notes: build succeeded 0 errors / 0 warnings. No DB schema change. Measurement-terms import validates JSON shape before touching anything (invalid file → warning dialog, no changes made). Database import is the more dangerous of the two — confirmed via dialog AND auto-backed-up, but a full app restart is still the safest way to guarantee every open view reflects the swapped data (not enforced, just recommended if anything looks stale after import).

### 2026-07-25 — Cancelled/returned refund state (UI + receipt + editor lock)  [DONE]
- Ask: "UI: gray out Cancelled/Returned records (lightest gray) and Completed records (a bit darker). Receipt: colour the balance status (已结清（已取货）green / 已结清（未取货）light green / 未结清 orange / 已退款或部分退款 red). Business logic for 已取消/已退货: in edit order, changing status to 已取消/已退货 sets 余额状态 to 已退款或部分退款, locks all services incl. 当前服务尾款已结清, marks all checkboxes (incl. 已取货) red with a stroke across the whole checkbox (not applicable) — but service details (e.g. custom measurement records) stay viewable. In the receipt, remove 剩余尾款 and show 余额状态 = 已退款或部分退款."
- Done:
  - [x] `Order.IsRefunded` (Cancelled/Returned) + `BalanceStatusKind` enum + `Order.PaymentStatusKind` (single source of truth: Refunded / Outstanding / ClearedPickedUp / ClearedNotPickedUp).
  - [x] Languages.xml (both blocks): `Payment.Status.Refunded` = Payment.Status.Refunded / "Refunded or Partially Refunded".
  - [x] `OrderPaymentSummaryConverter` Status mode now maps `PaymentStatusKind` → label (so list, detail panel and receipt all show Payment.Status.Refunded for cancelled/returned).
  - [x] MainWindow list: added `IsRefunded` DataTrigger (lightest gray #C3C9CF / opacity 0.5); kept `IsPickedUp` trigger (darker #9AA3AB / 0.7) for completed.
  - [x] Receipt (`AddReceiptTotals`): omit the `Order.Fields.FinalBalance` line when `IsRefunded`; colour balance status via new `ReceiptStatusLine` + `BalanceStatusBrush` (green/light green/orange/red).
  - [x] OrderEditWindow.xaml: `NotApplicableCheckBox` style (red box + red strikethrough label + red line across the whole control).
  - [x] OrderEditWindow.xaml.cs: `_isRefunded` field; `OnStatusChanged` toggles refund lock; `SetServiceControlsEnabled`/`ApplyRefundLockState`/`ApplyNotApplicableCheckboxStyle`; `RefreshPricingLocks` + `UpdateBalanceStatusDisplay` + PickedUp enabling respect `_isRefunded`; refunded style also applied when opening an already-cancelled/returned order (read-only). Customer fields + custom-made records list stay usable so measurements remain viewable.
- Notes: build succeeded 0 warnings / 0 errors. Balance status is computed (no DB change). Extracted `UpdateBalanceStatusDisplay` to keep `RefreshPaymentSummary` cognitive complexity ≤15. Source English-only; non-English only in Languages.xml.

### 2026-07-25 — Custom-service flag column + measurement printing  [DONE]
- Ask: "1. Remove last modified date time from the main records section; show it in order details (order still ordered by last modified). 2. UI: wider Final balance status column + wider Order number column. 3. Replace last modified column with a flag 定制服务: no→无, yes→有 with a second bracketed row (garment names) e.g. 有 /(西装、衬衣), centered, wrappable. 4. Printing: if the order has 定制服务, add under 本地配置>Print: 打印量身尺寸 and 打印小票和所有尺寸 (hide when no custom service); same on right-click menu. 5. Clicking either pops a small window asking language + unit for measurements; this is a PRINT method (not save-to-PDF)."
- Done:
  - [x] Languages.xml (zh-CN + en-US): `Order.Fields.CustomMadeFlag`, `CustomMade.Flag.Yes/No`, `Toolbar.PrintMeasurements`, `Toolbar.PrintReceiptAndMeasurements`, `MeasurePrint.Title/LanguagePrompt/UnitPrompt/Print/Cancel`.
  - [x] `Order.HasCustomMadeService` [NotMapped] — true when any custom-made record has a garment with a cm/inch measurement value.
  - [x] `Services/CustomMadeMeasurementReader` — `GetGarmentNames` (distinct, order-preserving) + `BuildSections` (per-garment term/value rows, unit-aware; per-garment extracted to `BuildGarmentSection` to keep cognitive complexity ≤15).
  - [x] `Converters/CustomMadeServiceFlagConverter` — ConverterParameter `Flag` (`CustomMade.Flag.Yes`/`.No`) / `Names` (bracketed garment names, zh sep 、 / en sep ", ") / `NamesVisibility`.
  - [x] MainWindow.xaml: removed LastModified column; added centered wrappable Custom Service CellTemplate column; widened OrderNumber 150→200 and BalanceStatus 140→180; added LastModified to detail panel (between OrderDate and Status); added print menu items (toolbar Print submenu + context menu) gated `Visibility` on `SelectedOrder.HasCustomMadeService` via BoolToVisibility.
  - [x] `Views/MeasurementPrintOptionsWindow` — language radios (from `AvailableLanguages`, default = current) + unit radios (cm default / inch); exposes `SelectedLanguageCode` + `IsInch` (auto-props set on Print to dodge x:Name S2325 false positive).
  - [x] MainWindow.xaml.cs — `OnPrintMeasurementsClick`/`OnPrintReceiptAndMeasurementsClick` (+ context variants) → `PrintMeasurements(includeReceipt)`; `BuildMeasurementDocument` (measurements-only + branding) and `AddMeasurementSections` (page-break-before when appended to receipt). Uses PrintDialog + FlowDocument (not QuestPDF); measurement language/unit from dialog, receipt stays UI language.
- Notes: build succeeded 0 warnings / 0 errors. Ordering unchanged (LoadOrdersAsync still defaults to LastModifiedDate desc). No DB migration. Source English-only; non-English only in Languages.xml.

### 2026-07-25 — Measurement Terms system (modular garment measurements)  [DONE]
- Ask: "Add a Measurements Terms system: predefine measurement terms (localized, locked) as dictionaries for bespoke tailoring body sections; classify garments (Jacket, Vest, Shirt, Pants, Blouse, Dress, Qipao); create a 3-column drag-drop mapping UI (left = garments ListView, center = assigned props, right = all props) with modern styling + icons; predefined fields locked, user can add custom props (edit/save/delete + view/edit alternative-language remapping popup). Then replace the static Jacket+Shirt measurement fields in the custom-made service window with garment preselection that loads only the related measurement fields per garment; handle unit switching; PDF export must include all selected garments and their props."
- Decisions (confirmed): garments are ALSO user-extensible — 7 predefined locked garments PLUS user-addable custom garments; persistence via JSON store under LocalAppData; legacy Jacket/Shirt fields kept for back-compat (records migrate to Garments on next save); alt-language remap via popup.
- Done:
  - New `Models/MeasurementTerm.cs`: `MeasurementTerm`, `GarmentType`, `MeasurementTermsConfig`, `MeasurementTermDefaults` (21 predefined term ids, 7 predefined garment ids + default garment→term maps, `CreateDefaultConfig`, `IsTermLockedInGarment`).
  - New `Services/MeasurementTermsService.cs`: singleton, persists `measurement-terms.json`; resolve/add/edit/delete custom terms & garments, add/remove term↔garment mapping (blocks locked), `LoadOrSeed`+`MergePredefined` version upgrade, `ConfigChanged`.
  - New `Views/MeasurementTermLanguageWindow.xaml(.cs)`: alt-language name editor popup (one row per available language).
  - New `Views/MeasurementTermsWindow.xaml(.cs)`: 3-column drag-drop mapping window (left garments / center assigned / right all props), modern styles + Segoe MDL2 icons, inline edit + lock + alt-language + delete + Add Garment/Measurement. Launched from Local Configuration menu.
  - `Models/CustomMadeServiceRecord.cs`: added `List<GarmentMeasurement> Garments` (+ `GarmentMeasurement`, `MeasurementValue`), kept legacy Jacket*/Shirt* fields.
  - `Views/CustomMadeServiceWindow.xaml(.cs)`: replaced static Jacket/Shirt grid with garment ToggleButton selector + dynamically generated per-garment measurement cards; cache-based cm/in dual-unit source of truth (`_valueCache`, `_termEditors`); unit switch persists+reapplies; language change rebuilds selector/cards; seeds from legacy fields for old records; `BuildGarmentsIntoRecord` writes Garments on save; PDF export iterates all selected garments/props (`BuildPdfGarmentSections`).
  - `Converters/CustomMadeRecordSummaryConverter.cs`: summary lists selected garment names from `Garments` (resolved via service), falls back to legacy fields.
  - `MainWindow.xaml(.cs)`: Measurement Terms (Measurement Terms) MenuItem under Local Configuration → opens MeasurementTermsWindow.
  - `Languages.xml` (zh-CN + en-US): `Measure.Term.*` (21), `Measure.Garment.*` (7), `Toolbar.MeasurementTerms`, `MeasureTerms.*`, `TermLanguage.*`, `Measure.SelectGarments`, `Measure.NoGarmentSelected`.
- Notes: build succeeded 0 errors / 0 warnings. Source code English-only; non-English strings only in Languages.xml. No DB migration (Garments serialized into existing `Order.CustomMadeRecordsJson`).


### 2026-07-24 — Payment UI beautify / de-nest (OrderEditWindow)  [DONE]
- Ask: "Beautify the Payment components. currently the sections are so nested, organize and categorize them well."
- Done (XAML only, `Views/OrderEditWindow.xaml`):
  - Added reusable `Window.Resources` styles: `SectionCard`, `SummaryCard`, `SectionHeading`, `PaymentCard`, `PaymentTitle`, `StepLabel`, `MethodRadio`, `StepDivider`, `AccentBar`.
  - Converted all top-level section borders (service-type selector, Alterations / CustomMade / ReadyMade panels, Notes) to the white rounded `SectionCard`; totals summary uses `SummaryCard`; headings use `SectionHeading`.
  - Rebuilt the 3 payment sub-cards with a colored accent bar + title header, `StepLabel`-styled deposit/final method labels, `MethodRadio`-styled radios, and a `StepDivider` at the top of each `FinalBlock` so the deposit and the final balance read as two clear steps.
  - Preserved every `x:Name` and event handler (no code-behind changes) — the divider lives inside FinalBlock so it only shows with the final step.
- Notes: build succeeded 0 warnings / 0 errors.

### 2026-07-24 — Currency: per-order → global app setting  [DONE]
- Ask: "Moving the currency setup from record base into global base... small business should rely on the setup globally, no complicated logic. Currency setup moving to local configurations menu bar."
- Decisions (confirmed with user): keep the `Orders.CurrencyType` DB column but ignore it (no migration); offer CAD/USD/CNY; currency entry lives directly under Local Configuration (sibling of Add or Change Header & Footer).
- Done:
  - New `Services/CurrencySettingService.cs`: singleton `Instance` (INotifyPropertyChanged) with `Current` (CurrencyType), `Symbol` (￥ for CNY else $), `SetCurrency` + `CurrencyChanged`; persists to `currency-setting.json` under LocalAppData (mirrors LanguagePreferenceStore).
  - New `Views/CurrencySettingWindow.xaml(.cs)`: small dialog (currency ComboBox + Save/Cancel) launched from Local Configuration.
  - `MainWindow.xaml`: added `Currency Setup` MenuItem under Local Configuration (Click=OnCurrencySettingClick); removed the per-order currency detail row.
  - `MainWindow.xaml.cs`: `OnCurrencySettingClick` opens dialog and reloads orders on change; receipt symbol + currency line now use `CurrencySettingService.Instance`.
  - Converters `CurrencyAmountConverter` (dropped 2nd currency value/`ParseCurrency`) and `OrderPaymentSummaryConverter` now read the global symbol.
  - `OrderEditWindow.xaml(.cs)`: removed the currency panel/`CurrencyBox` and all its wiring (Initialize/Refresh/CreateCurrencyItem/GetSelectedCurrencyType), dropped `CurrencyType` from `OrderSaveData`/`ApplyEditableFields`; `FormatCurrency` now static using global symbol.
  - `Languages.xml`: added `Toolbar.CurrencySetting` (Currency Setup / Currency Setup) and `Currency.Title` / `Currency.Prompt` to both blocks.
- Notes: build succeeded 0 warnings / 0 errors. Per-order CurrencyType column retained but unused; global money bindings refresh on next order load.

### 2026-07-24 — Application Menu: Local Configuration dropdown  [DONE]
- Ask: "Application Menu update — 1. Add 本地配置 auto-dropdown on the navigation. Move 页眉页脚 to the item, update wording to 添加或更改页眉页脚. Add 本地数据库 as another auto expansion. Move 复制数据库路径, 定位数据库文件 and 打开数据库目录 into it."
- Done:
  - `MainWindow.xaml`: replaced the standalone Header & Footer button and the three data buttons (CopyDataPath / RevealDataFile / OpenDataFolder) with a `Menu` → top-level `Local Configuration` MenuItem containing `Add or Change Header & Footer` (Click=OnEditBrandingClick) and a nested `Local Database` submenu (auto-expand) holding the three DB items (reused OnCopyDataPathClick / OnRevealDataFileClick / OnOpenDataFolderClick — no code-behind change).
  - `Languages.xml`: reworded `Toolbar.HeaderFooter` Header & Footer→Add or Change Header & Footer / "Header & Footer"→"Add or Change Header & Footer"; added `Toolbar.LocalConfig` (Local Configuration / Local Configuration) and `Toolbar.LocalDatabase` (Local Database / Local Database) to both blocks.
- Notes: XAML-only + string table; existing click handlers reused. Build succeeded 0 errors.


### 2026-07-24 — Per-portion payment tax: 定金/实收定金 & 尾款/实收尾款 split  [DONE]
- Ask: "定金需要拆分成 定金和实收定金；尾款拆分成 尾款和实收尾款。现金/电子转账时定金=实收定金；定金付卡则实收定金=定金+税。尾款同理，由其支付方式决定。"
- Decisions (confirmed with user): entered 定金/尾款 are PRE-TAX; card adds tax on top of that portion only; 尾款 base = subtotal − 定金; tax on final by final method only; show split in detail panel + receipt + edit window.
- Done:
  - `Order.cs`: new `SectionPayment` record struct + `public static CalculateSectionPayment(subtotal, deposit, ratePercent, downMethod, finalMethod)` — tax applied to a portion only when THAT portion's method is Card; deposit clamped to subtotal. Section props now delegate to `AlterationMoney`/`ClothingMoney`/`CustomMadeMoney` (Total/Tax). New `ReceivedDownpayment` (Σ 实收定金). `FinalBalance`/`ReceivedFinalBalance` now use `FinalCharge` (taxed). `IsSectionCleared/SectionResidual/SectionReceivedFinal` rewritten to take `SectionPayment` (cleared = FinalBase≤0 or manual clear).
  - `OrderEditWindow.xaml.cs`: `_alterationMoney/_clothingMoney/_customMadeMoney` fields; `Refresh*Totals` compute via `Order.CalculateSectionPayment`; `RefreshPaymentSummary` shows received deposit (实收定金) + taxed final; `AutoCompleteSection`/`IsOrderBalanceCleared` compare deposit against pre-tax subtotal base.
  - `OrderEditWindow.xaml`: summary row1 label → `Order.Fields.ReceivedDownpayment`.
  - `MainWindow.xaml` detail panel: shows 定金(TotalDownpayment) + 实收定金(ReceivedDownpayment) + 实收尾款(ReceivedFinalBalance) + 剩余尾款(FinalBalance).
  - `MainWindow.xaml.cs` receipt totals: 定金 always; 实收定金 only when ≠ nominal; 实收尾款; 已收税额; 剩余尾款.
  - `Languages.xml`: added `Order.Fields.ReceivedDownpayment` (实收定金/Received Downpayment); `ReceivedFinalBalance` value 已收尾款→实收尾款.
- Notes: generalizes old "card anywhere taxes whole section" → per-portion; identical results when both portions share a method. Persisted `TotalAmount` recomputed on save; legacy mixed-method orders keep their stored TotalAmount (breakdown recomputes). Sonar-clean (Order.cs, OrderEditWindow.xaml.cs, MainWindow.xaml.cs), build 0 errors.

### 2026-07-24 — Receipt wording + paid tax + receipt UI polish  [DONE]
- Ask: "wording update: 尾款（余额）-> 剩余尾款. Adding paid final balance for the receipt. Adding Paid tax field to receipt. UI improve in PDF receipt: enlarge size for Service type, bold; slighter border among services; last services for slight border; remove the printing time for the PDF."
- Done:
  - `Order.cs`: added `[NotMapped] TotalTax => AlterationTax + ClothingTax + CustomMadeTax`.
  - `Languages.xml`: `Order.Fields.FinalBalance` zh 尾款（余额）→剩余尾款 (English kept); added `Order.Fields.PaidTax` (zh 已付税额 / en Paid Tax) to both blocks.
  - `MainWindow.xaml.cs` receipt (`BuildReceiptDocument`): paid final balance already present via `ReceivedFinalBalance` line (kept); added guarded Paid Tax line (`TotalTax > 0`) in `AddReceiptTotals`; enlarged `ReceiptSectionTitle` 11→14 (bold); new `ReceiptServiceDivider()` (light #E6E6E6, 0.7px) appended to every service section incl. last; removed heavy pre-totals `ReceiptDivider()` and the app-generated `Receipt.PrintedAt` line; removed dead guard in `AddAlterationReceiptSection`.
- Notes: Sonar-clean (both files), build succeeded 0 errors.

### 2026-07-24 — Logo placement + full-field receipt + header-driven title  [DONE]
- Ask: "Improve header/footer branding — add left/center/right option for logo
  placement; receipt must print ALL fields from the order details on the main app;
  no Title on the receipt unless the Header editor is empty; apply the same title
  rule to measurements downloading."
- Done:
  - `ReceiptBrandingStore`: new `LogoPlacement { Left, Center, Right }` enum +
    `ReceiptBrandingSettings.LogoPlacement` (default Center), persisted in JSON.
  - `BrandingRenderer`: `CreateLogoBlock` now takes a `LogoPlacement` and sets the
    FlowDocument image/block alignment; new `AlignLogo(IContainer, placement)` helper
    for the QuestPDF logo item.
  - `ReceiptBrandingWindow.xaml`/`.cs`: added a "Logo Position" radio row
    (Left/Center/Right) in the logo card; loads from settings, saved on OK (inline
    `GetValueOrDefault()` chain — avoids S1125/S3358).
  - `MainWindow`: receipt now prints Status, CurrencyType, ServiceType (services
    summary converter), per-section Tax lines, PaymentBreakdown (multi-line) and
    Notes — matching the detail panel. Default shop title (`Main.HeaderTitle` +
    `Receipt.Title`) is only emitted when the header editor is empty. Reused the
    detail-panel converters (`OrderServicesSummaryConverter`,
    `OrderPaymentSummaryConverter`). Logo injected with the stored placement.
    `BuildReceiptDocument` decomposed into per-section helpers (cognitive complexity).
  - `CustomMadeServiceWindow.SaveMeasurementsPdf`: logo aligned via
    `BrandingRenderer.AlignLogo`; the `Customer.Measurements.PrintTitle` heading is
    skipped when the header editor has content.
  - `Languages.xml`: added `Branding.LogoPlacement` + `Branding.Placement{Left,
    Center,Right}` to both zh-CN and en-US blocks.
- Notes: `dotnet build` → 0 errors; SonarLint clean. GOTCHA: SonarLint flags WPF
  x:Name-only helper methods as S2325 "make static" (false positive — would break
  the build); inline them into the instance method instead. For `bool?` conditions
  use `.GetValueOrDefault()` (not `== true`/`is true`) to avoid S1125.

### 2026-07-24 — Receipt/measurements header & footer branding editor  [DONE]
- Ask: "New feature — Inject Word editor for the main application, which is able
  to Add context to preset the header and footer. Purpose: Inject the context for
  printing receipt. Apply the same header and footer for measurements downloading
  as well. Requiring: Beautify the editor, and easy to modify and save."
- Design (from clarifying Q&A): rich text (bold/italic/underline/font size/
  alignment/color); logo image in header; separate content per language (zh/en);
  footer applied to BOTH receipt and measurements PDF.
- Done:
  - `Services/ReceiptBrandingStore.cs` (NEW, static class): loads/saves
    `receipt-branding.json` + manages logo file under
    `%LocalAppData%\LeeYongeOrdering\Branding`. `ReceiptBrandingSettings` holds
    per-language `LocalizedBranding { HeaderXaml, FooterXaml }` + `LogoFileName`.
  - `Services/BrandingRenderer.cs` (NEW): round-trips rich content between
    `RichTextBox` FlowDocument ↔ XAML string (`XamlWriter.Save`/`XamlReader.Parse`)
    ↔ receipt FlowDocument, and renders XAML → QuestPDF text spans
    (`RenderToPdf`, walks Paragraphs/Inlines, maps Bold/Italic/Underline/FontSize/
    Foreground/alignment).
  - `Views/ReceiptBrandingWindow.xaml/.cs` (NEW): beautified editor — logo card
    (choose/remove), formatting ribbon (B/I/U, font size, align L/C/R, color
    swatches), per-language tabs each with header+footer `RichTextBox`.
  - `MainWindow`: toolbar button `Toolbar.HeaderFooter` → opens editor;
    `InjectReceiptBranding` prepends header + logo and appends footer to the
    printed receipt FlowDocument.
  - `CustomMadeServiceWindow.SaveMeasurementsPdf`: renders logo + header before
    title and footer after sections in the measurements PDF.
  - `Languages.xml`: added `Toolbar.HeaderFooter` + `Branding.*` keys to BOTH
    zh-CN and en-US blocks.
- Notes: `dotnet build` → 0 errors; SonarLint clean after fixes (made
  `ReceiptBrandingStore` static per S2325, decomposed `RenderParagraph` for
  cognitive complexity, dropped `_isPopulating` in favor of `IsLoaded`, fixed a
  merged `return; try` line in `MainWindow`). GOTCHA: QuestPDF + WPF + HotChocolate
  under ImplicitUsings collide on `Path`/`Color`/`FontWeight`/`FontStyle`/
  `HorizontalAlignment` — alias `Path` and fully-qualify `System.Windows...`.

### 2026-07-24 — Custom-made cleared: relabel edit button + gray out locked view fields  [DONE]
- Ask: "BUG Fix — The 修改定制记录 button still not changed to 查看定制记录. The 查看定制记录 panel, the locked fields should gray out like the other places."
- Done:
  - `OrderEditWindow`: extracted `RefreshCustomMadeButtonLabel()` — sets the
    record button to View vs. Edit key using
    `_isReadOnly || CustomMadeBalanceClearedCheck.IsChecked is true`. Called from
    `RefreshLocalizedLabels` AND `RefreshPricingLocks`, so toggling the section
    cleared checkbox live relabels 修改定制记录 → OrderEdit.ViewCustomMade (prev only reacted to
    whole-order read-only).
  - `CustomMadeServiceWindow.xaml`: added the implicit `TextBox` read-only style
    (copied from `OrderEditWindow.xaml`) — `IsReadOnly=True` → Background `#F0F0F0`,
    Foreground `#808080`, so view-mode boxes gray out consistently with the order
    editor. (Radios/document buttons already gray via `IsEnabled=false`.)
- Notes: `dotnet build` → 0 errors; SonarLint clean. Files: `OrderEditWindow.xaml.cs`,
  `CustomMadeServiceWindow.xaml`.

### 2026-07-24 — Custom-made record: open read-only (OrderEdit.ViewCustomMade) when section balance cleared  [DONE]
- Ask: "BUG Fix — For Custom made service, if the current payment final balance is cleared, then 1. 修改定制记录 -> 查看定制记录; 2. all records fields should be locked, including the upload image area."
- Done: `OrderEditWindow.OnEditCustomMadeRecordClick` now computes
  `recordReadOnly = _isReadOnly || CustomMadeBalanceClearedCheck.IsChecked is true`
  (same gate as `RefreshPricingLocks`) and passes it as `isReadOnly` to
  `CustomMadeServiceWindow`, and uses it for the open-validation skip + view-only
  ShowDialog path. Reuses the window's existing `ApplyReadOnlyMode`
  (title → `OrderEdit.ViewCustomMade` "OrderEdit.ViewCustomMade", all boxes/radios read-only,
  Save hidden, ReadOnlyNotice shown) and `CanEditDocuments => !_isReadOnly` which
  already gates the upload/replace/delete document buttons — so the image upload
  area locks too. No new keys, no model change.
- Notes: `dotnet build` → 0 errors; SonarLint clean on the changed file. Add
  button was already disabled for a cleared section via `RefreshPricingLocks`.

### 2026-07-24 15:30 — Records list → ListView+GridView, adjustable text, taller rows, focus fade, Enter/gray-out fixes  [DONE]
- Ask: "The Look and UI in records section still not look well. but the tooltip is good designed. A. main record section should use a different Grid system to populate data. B. Records text should be adjustable in the filter area, 20px by default. C. Records should be larger/wider for the column height. D. Add a transition switching between records. Accessibility: A. key ENTER should open view/edit order panel (currently behaves like arrow down). B. Gray out the completed records."
- Decisions (from clarifying Qs): A → replace DataGrid with **ListView + GridView columns**; D → the **focus/selection color on the record entry fades in and out** (opacity animation on selection change).
- Done:
  - Replaced orders `DataGrid` with `ListView` + `GridView` (8 columns; `DisplayMemberBinding` for text cols, `CellTemplate` MultiBinding for TotalAmount, converters for Status/Balance).
  - Added `OrderListItemStyle` (ListViewItem) with custom `ControlTemplate`: `MinHeight=54` + padding (C), hover bg, `GridViewRowPresenter`; `IsSelected` Enter/ExitActions run `DoubleAnimation` fading a `Highlight` bg + left `Accent` bar in/out (D); `DataTrigger` on `Status==Completed` grays `TextElement.Foreground` + 0.7 opacity (Acc B).
  - Added `OrderListHeaderStyle` (GridViewColumnHeader) — dark header bar.
  - Font-size `Slider` (`RecordFontSizeSlider`, min12/max40/**default 20**) + `px` readout in filter bar; `ListViewItem.FontSize` binds to slider (B).
  - Context menu moved to `ListView.ContextMenu`; `OnOrderRowRightClick` cast `DataGridRow`→`ListViewItem`.
  - Enter fix (Acc A): ListView has no built-in Enter navigation, so existing `OnOrderRowKeyDown` Enter→`OnEditOrderClick` now opens the editor cleanly.
  - `OnOrdersListSizeChanged` auto-fills the trailing Notes column.
  - Localization key `Filter.RecordFontSize` added to both zh/en blocks.
- Notes: `dotnet build` → **0 errors**. Files: `MainWindow.xaml`, `MainWindow.xaml.cs`, `Languages.xml`. `DataGridRow`→`ListViewItem` cast is the key §11 gotcha for future DataGrid→ListView conversions.

### 2026-07-24 — Wider main rows + document upload for custom-made records  [DONE]
- Ask: "1. All records in the main application should have a wider height of the
  row. 2. Add a new upload and checking documents for each record's custom made
  record, categories: Handwriting receipts, Fabrics selected, Photos, or others.
  Global helpers for upload/download/checking photo; each record may support
  multiple documents (e.g. multiple fabrics); user can View in app, download,
  delete, and replace (override) images."
- Done:
  - `MainWindow` DataGrid `RowHeight="40"` + `VerticalContentAlignment=Center`.
  - `CustomMadeDocument` model (+ `CustomMadeDocumentCategory` enum); `Documents`
    list on `CustomMadeServiceRecord`; deep-copied in `Clone`.
  - `Services/DocumentStorageService` global helper — import/export/delete under
    `AppData\LeeYongeOrdering\Documents\CustomMade`.
  - `Views/DocumentPreviewWindow` in-app image viewer (BitmapImage OnLoad).
  - Documents section in `CustomMadeServiceWindow` (4 categories, multi-doc,
    Upload/View/Download/Replace/Delete). Transactional: uploads copy to store
    immediately, commit on Save, `OnClosed` rolls back added files when not saved;
    deletes of already-saved files apply on Save. Edit buttons gated by
    `CanEditDocuments` (`!_isReadOnly`).
  - Localization keys added to both blocks (`CustomMade.Documents.*`).
- Gotchas:
  - `Path` is ambiguous (`HotChocolate.Path` via ImplicitUsings) — alias with
    `using Path = System.IO.Path;`.
  - Image bytes live on disk; only references persist in the record JSON → no DB
    migration needed.
- Build verified: `dotnet build` succeeded, 0 errors.

### 2026-07-24 — Lock pricing (price/tax) when a section balance is cleared  [DONE]
- Ask: "when final balance cleared for the service, the payment section such as
  price, tax these area should be locked."
- Done: added `OrderEditWindow.RefreshPricingLocks()` (+ `SetClothingRowsLocked`),
  called at the END of `RefreshComputedTotals` (after the `Refresh*Totals` passes
  re-enable tax boxes, so the lock wins). Per section, when its
  `BalanceClearedCheck` is ticked (or the order is read-only) it makes the price
  box + tax box read-only and disables the item/record editors that feed the total
  (clothing rows + Add Item, Add/Remove Custom-Made). Complements the existing
  `ApplySectionLock` which locks the payment radios/deposit controls.
- Gotcha: `Refresh*Totals` sets `*TaxBox.IsEnabled = cardUsed` every pass, so any
  pricing lock MUST run after them or it gets clobbered.
- Build verified: `dotnet build` succeeded, 0 errors.

### 2026-07-24 — Clear-all must respect a forced final-balance method  [DONE]
- Ask: clearing all final balances globally overrode a manually forced per-section
  final-balance method (e.g. deposit by card, final forced to cash) back to the
  deposit method.
- Done: `OrderEditWindow.ApplyClearAllToSection` now only defaults the final method
  from the deposit method when `GetSelectedPaymentMethod(final…)` is null (user
  hasn't chosen one). An existing manual selection is preserved.
- Build verified: `dotnet build` succeeded, 0 errors.

### 2026-07-24 — Relocate app icon to Assets/ICONS + photo welcome header  [DONE]
- Ask: "1. UI: Update the application icon to the following path
  C:\Projects\LeeYongeOrdering\Assets\ICONS, use relative path. 2. for the welcome
  panel, the main header image use ...\Assets\WELCOME PANEL\welcome_header_enter_system.jpg
  this image as the header image."
- Done:
  - Moved icon refs to `Assets\ICONS\app-icon.ico`: `csproj` `<ApplicationIcon>` +
    `<Resource>`; `MainWindow`/`LanguageSelectionWindow` `Icon="/Assets/ICONS/app-icon.ico"`.
    Deleted the stale root-level `Assets/app-icon.*` duplicates.
  - Language window banner now shows the tailoring-shop photo
    (`Assets/WELCOME PANEL/welcome_header_enter_system.jpg`) as a full-bleed header
    with a dark bottom gradient scrim behind the Welcome text.
- Gotcha: WPF embeds resources with the folder space URI-escaped and lowercased
  (key = `assets/welcome%20panel/...`). Reference it in XAML with `%20`
  (`/Assets/WELCOME%20PANEL/...`) so the pack lookup matches. Verified by dumping
  `*.g.resources` keys from the built dll.
- Build verified: `dotnet build` succeeded, 0 errors.

### 2026-07-24 — App SVG icon + beautified language selection panel  [DONE]
- Ask: "1. UI: Create a SVG icon for the application, and applied to the application
  exe. Create a folder for images the application. Later on we are using image for
  welcome panel. 2. beautify the language selecting panel. > adding welcome message.
  > Dropdown language selection changes to radio button > add a button called 进入系统"
- Done:
  - Added `Assets/` folder holding `app-icon.svg` (source design: white clothes
    hanger on indigo→violet gradient rounded square) and `app-icon.ico`
    (multi-res 16–256, PNG-in-ICO, generated via a throwaway GDI+ script that
    mirrors the SVG — no SVG rasterizer installed on the box).
  - `csproj`: `<ApplicationIcon>Assets\app-icon.ico</ApplicationIcon>` +
    `<Resource Include="Assets\app-icon.ico" />`. `MainWindow` and the language
    window set `Icon="/Assets/app-icon.ico"`.
  - `LanguageSelectionWindow` redesigned: gradient welcome banner (icon + Welcome +
    subtitle), ComboBox replaced by radio buttons generated in code-behind from
    `AvailableLanguages` (GroupName `LanguageGroup`), selecting a language switches
    the UI live; styled full-width "LanguageSelection.Enter" button confirms.
  - New localization keys (both blocks): `LanguageSelection.Welcome`,
    `LanguageSelection.WelcomeMessage`, `LanguageSelection.Enter`.
- Gotcha: WPF exe icons must be `.ico` (SVG can't be used directly); ICO built with
  PNG-compressed entries so all sizes ship in one file.
- Build verified: `dotnet build` succeeded, 0 errors.

### 2026-07-24 — Lock payment section once final balance is cleared  [DONE]
- Ask: "if the final balance is cleared, it is not allowed to change the payment
  section anymore, the payment section should be locked."
- Done: `OrderEditWindow.ApplySectionLock(PaymentSectionControls)` called from
  `UpdatePaymentVisibility` for all 3 sections. When a section's
  `BalanceClearedCheck` is ticked (or the whole order is read-only), disable the
  deposit-method radios, deposit box, deposit-received check, and final-method
  radios. The cleared checkbox stays enabled (unless read-only) so the section can
  be un-cleared to edit again. Locks/unlocks consistently for manual, auto-complete,
  and clear-all paths since all route through `UpdatePaymentVisibility`.
- Build verified: `dotnet build` succeeded, 0 errors.

### 2026-07-24 — Main UI: 2K window, row context menu, copy-order, delete confirm & keyboard  [DONE]
- Ask: "1. UI: make the main application the larger to size 2K full screen 2. on the main console. each record right click ... copy/delete/edit/print receipt options. > if the order status showing completed/returned/canceled, after the copy, remove the picked up tick, also mark the order status to processing. 3. for any Delete action of record, it should pop up alert that asking user if wants delete. Accessibility fix: 1. the Enter key on record should show the record details. 2. The DEL key pressed, should trigger Delete order action."
- Plan:
  - [x] Maximize main window at 2K default (`WindowState="Maximized"`, 2560×1440)
  - [x] Add `DataGrid.ContextMenu` (Edit/Copy/Delete/Print) + `PreviewMouseRightButtonDown` row-select
  - [x] `MainViewModel.CopyOrderCommand` / `CopyOrderAsync` (deep-copy, closed→Processing)
  - [x] Confirm dialog already in `DeleteOrderAsync` — route menu + DEL through it
  - [x] Enter opens details; Delete key → delete command (single `KeyDown` switch)
  - [x] Localization keys + build
- Notes: `MainWindow.xaml` window set `WindowState="Maximized"` Height/Width 1440/2560. Context menu moved **to `DataGrid.ContextMenu`** (Click handlers on a menu inside a row-`Style` `Setter.Value` fail to compile — MC6007 mis-attributed to `DataGridTextColumn`; see SKILL §11); row `Style` keeps only an `EventSetter` for `PreviewMouseRightButtonDown` → `row.IsSelected = true`. Handlers `OnContextEdit/Copy/Delete/PrintClick` reuse existing commands/handlers. `MainViewModel.CopyOrderAsync` loads source `AsNoTracking()`+`Include(Items)`, copies all scalars (no `Id`), new `ORD-{timestamp}` number + `UtcNow`, deep-copies `Items`, resets `Completed/Cancelled/Returned` → `Processing` (also clears the status-derived "picked up" tick), saves, reloads, re-selects the copy. `OnOrderRowKeyDown` now a switch: Enter→edit, Delete→`DeleteOrderCommand` (confirm dialog owned by the command). New Languages.xml keys (both blocks): `Toolbar.CopyOrder`, `Status.CopySucceeded`, `Status.CopyFailed`. Build succeeded 0 errors.

### 2026-07-24 — Receipt & detail: hide zero/no-deposit services  [DONE]
- Ask: "in the receipt section on the order details, for any services that service is 0 or deposit no yet selected, it can considered the services not added. shouldn't display on the receipt" + follow-up "Still having a bug that Alteration service always display ... even though the price is 0. double check".
- Plan:
  - [x] Gate each receipt section on total > 0 AND deposit method selected
  - [x] Fix detail panel showing Alterations whenever `ServiceDetails` set (ignored price)
  - [x] Share one gate between receipt and detail panel
- Notes: Added `[NotMapped]` `AlterationAddedToReceipt`/`ClothingAddedToReceipt`/`CustomMadeAddedToReceipt` to `Order` (`XxxTotal > 0m && XxxDownpaymentMethod is not null`). `BuildReceiptDocument` gates the three sections on them (reduced cognitive complexity by using the properties). Root cause of the "Alteration always shows" bug was the **detail panel** in `MainWindow.xaml`: the Alterations `Border.Visibility` was bound to `ServiceDetails` (non-null), ignoring price/deposit; Ready-made/Custom-made used `Items.Count`/`Records.Count` triggers. Rebound all three section borders to the shared `XxxAddedToReceipt` props via a newly registered built-in `BooleanToVisibilityConverter` (`BoolToVisibility`). Build succeeded 0 errors. (Note: these builds used `dotnet build`; SonarLint was not separately run this session.)

### 2026-07-23 — Alteration service category dropdown  [DONE]
- Ask: "Add dropdown details for alteration services. > Garment Adjustments > Others"
- Decisions (from clarifying Qs): replace the free-text Service Details box with the dropdown; show the selected category in both the detail panel and printed receipt.
- Plan:
  - [x] Add localization keys Alteration.Category.GarmentAdjustments / .Others (both blocks)
  - [x] Replace AlterationServiceDetailsBox TextBox with AlterationCategoryBox ComboBox (2 ComboBoxItems, Tag = stable token)
  - [x] Editor load: select item whose Tag matches existing.ServiceDetails; save: store selected Tag into ServiceDetails
  - [x] Detail panel + receipt: map token → localized text
  - [x] SonarLint before build, then build
- Notes: Category stored in the existing `Order.ServiceDetails` column as a stable token (`GarmentAdjustments`/`Others`) — no new DB column. `OrderEditWindow.xaml` swaps the TextBox for a ComboBox; load/save inlined in `SetupForEdit`/`ApplyEditableFields` (avoids S2325 false positive on XAML-field-only helpers). Detail panel (`MainWindow.xaml`) and receipt (`BuildReceiptDocument`) render the localized name via the existing `LocalizationLookupConverter` / `LocalizeWithFallback` with prefix `Alteration.Category` (legacy free-text falls back to the raw stored string). SonarLint clean; build succeeded 0 errors.
- Follow-up (2026-07-23): per new skill rule (§5 dropdown default), the category dropdown now defaults to the **first** option Garment Adjustments/Garment Adjustments — `SelectedIndex = 0` on the new-order path and a fallback to the first item on edit-load when the stored value matches no option.

### 2026-07-23 — Show tax in order detail panel  [DONE]
- Ask: "1. 订单明细中， 如果有税收，要把税收给写上去。" (Order detail panel: if a section has tax, show the tax amount.)
- Plan:
  - [x] Add [NotMapped] AlterationTax/ClothingTax/CustomMadeTax (= Total - Subtotal) to Order
  - [x] Add PositiveAmountToVisibilityConverter (decimal > 0 → Visible)
  - [x] Add a Tax row (Order.Fields.TaxAmount) between Subtotal and SectionTotal in each detail section, visible only when tax > 0
  - [x] SonarLint before build, then build
- Notes: `Order.cs` gained `[NotMapped] AlterationTax/ClothingTax/CustomMadeTax` (section Total − Subtotal). New `Converters/PositiveAmountToVisibilityConverter.cs` (decimal/double/int > 0 → Visible else Collapsed), registered in `MainWindow.xaml` as `PositiveAmountToVisibility`. Added a conditional Tax row (label `Order.Fields.TaxAmount`, amount via `CurrencyAmountConverter`) in all three detail sections — Alterations & Ready-made between Subtotal and section Total, Custom-made before section Total (it has no Subtotal row). No new localization keys (reused `Order.Fields.TaxAmount` “税额”/“Tax”). SonarLint clean; build succeeded 0 errors.

### 2026-07-23 — cm/inch unit toggle + localized measurement download  [DONE]
- Ask: "1. add a converter radio button cm and inch. for the measurements section in Custom made Service. > cm is by default > clicking on the inch will convert the specs into inch. if you see values like 20+ or 20-，only covert the digits. 2. Improve the Downloading measurement feature in custom made service. >add a new section under Custom Price section called \"Downloading Measurement\" >Radio button to select by local. >add submit to download measurement by localization >Becareful on the cm or inch selected. the download submit should match the selection. 3. The downloading file name formatter: > replace all empty space with \"_\" > End with language short name, zh / en"
- Plan:
  - [x] T1 cm/inch radios in Measures section (cm default), convert 8 boxes on toggle (digits only, keep +/-), convert inch→cm on save
  - [x] T2 "Download Measurement" section under Custom Price: language radios (zh/en) + submit; PDF localized to selection & matching unit
  - [x] T3 filename formatter: spaces→_, end with zh/en short name
  - [x] Add localization keys (both blocks); LocalizationService.GetText per-language
  - [x] SonarLint before build, then build
- Notes: **T1** Measures header → `DockPanel` title + right unit radios `CmRadio`(default)/`InchRadio` (`Checked=OnUnitChanged`). `_isInch` state, `CentimetersPerInch=2.54m`, `MeasurementNumberPattern=^(\d+(?:\.\d*)?)([+-]?)$`; `ConvertMeasurement` converts only the digit group (round 2, `0.##`), reattaches +/-; canonical cm storage via `MeasurementForStorage` (inch→cm on save). **T2** new Border after Custom Price: `DownloadChineseRadio`(default)/`DownloadEnglishRadio` + submit `OnDownloadSubmitClick` (`zh-CN`/`en-US`); default radio from `CurrentLanguageCode`; footer download button removed; `SaveMeasurementsPdf(filePath, languageCode)` uses `L(key)=GetText(key,lang)`, adds Unit info row; language-aware `GetModeLabel/GetAgeGroupLabel/GetAgeTypeLabel(...,lang)` + shared `AgeTypeKey`. **T3** `BuildPdfFileName(langCode)`: sanitize invalid→_, `Regex.Replace(@"[\s_]+","_").Trim('_')`, append `_{zh|en}`. New `LocalizationService.GetText(key, languageCode)`. New keys both blocks: `OrderEdit.Panel.DownloadMeasurement`, `Measure.Unit.Label/Cm/Inch`, `Download.Language.Label/Chinese/English`, `OrderEdit.DownloadSubmit`. Gotchas: `Path` ambiguous (HotChocolate vs System.IO) → qualify `System.IO.Path`; S1125 on consumed `bool?` → `.GetValueOrDefault()`; overloads-must-be-adjacent. SonarLint clean; build succeeded 0 errors.

### 2026-07-23 — Custom-made mode rename, section tax, accessibility & validation  [DONE]
- Ask: "1. Rename 量体->只量身， 从头定制->定制量身， reorder them, 定制量身is default. > reorder for the English as well. 2. UI, vertical aligned middle for 定制方式 options. now the radio buttons are top 3. features update: > Move tax rate out from the Custom made panel (not pre-locked in item level), the tax automation should be same as other services. 4. accessibility changes: > when navigate to record, hit KEYBOARD enter, it should pop up light box when double click events happened. > The current active pop up can be closed by keyboard ESC > For deposit input box section, when clicking into, the inital value of 0 should be removed(e.g. to avoid type value like 060), but if you click any element out without valid input, it should become 0 again. 5. Validation and formatter. Note: Message display for validation is not required, unless is mentioned. > pricing sections should only accept proper money input, use regular expression to resolve this. > email validation, if no valid email input, display red error message beaneath the input. > phone number validation. use the common validation for phone number. Error message rule is applied > Measurements validation: rule(start with number, accept only one \".\", accept end with +/- optional)"
- Decisions: English labels Measure Only / Full Custom (Full Custom default); phone = loose 7-15 digits allowing + and separators; custom-made tax defaults to 13% at section level, per-record tax dropped.
- Plan:
  - [x] T1 rename+reorder modes (Languages.xml both blocks, CustomMadeServiceWindow.xaml order, default CustomFromScratch in record model/editor)
  - [x] T2 vertical-center custom-made-mode radios
  - [x] T3 move tax to custom-made SECTION level (Order.CustomMadeTaxRate + column guard, editable CustomMadeTaxBox, drop record tax)
  - [x] T4a Enter opens editor (custom-made list + main orders grid)
  - [x] T4b ESC closes popups (IsCancel on Cancel buttons)
  - [x] T4c deposit box clears 0 on focus, restores on blur
  - [x] T5a money regex on pricing inputs (2 decimals)
  - [x] T5b email validation + red message beneath EmailBox
  - [x] T5c phone validation + red message beneath PhoneNumberBox
  - [x] T5d measurements regex (start digit, one dot, optional trailing +/-)
  - [x] SonarLint before build, then build; update context.md
- Notes: **T1** Languages.xml both blocks (Measure Only/Full Custom, Measure Only/Full Custom); `CustomMadeServiceWindow.xaml` radios reordered (Full Custom first, `IsChecked="True"`); `CustomMadeServiceRecord.ServiceMode` default + editor `InitializeMode(... ?? CustomFromScratch)`. **T2** mode `StackPanel` + both radios `VerticalAlignment="Center"`. **T3** `Order.CustomMadeTaxRate` (+ `App.xaml.cs` column guard `CustomMadeTaxRate TEXT NULL`), `CustomMadeSubtotal`/`CustomMadeTotal` now mirror Alteration/Clothing section-tax pattern; `OrderEditWindow` `CustomMadeTaxText`→editable `CustomMadeTaxBox` (13% default, card-driven enable via `RefreshCustomMadeTotals`), persisted in `ApplyPaymentFields`; per-record Tax/SumTotal UI removed from `CustomMadeServiceWindow` (record `TaxRate` left null for back-compat). Legacy orders show no custom-made tax until re-saved (accepted, consistent w/ other sections). **T4a** `OnCustomMadeRecordsKeyDown` + `MainWindow.OnOrderRowKeyDown` (Enter → edit). **T4b** `IsCancel="True"` on both Cancel buttons. **T4c** `RegisterDepositBox` + `OnDepositBoxGotFocus`/`LostFocus` (clears leading 0 on focus w/ `_syncingPayment` guard, restores "0" on blur). **T5a** `DecimalInputPattern` → `^\d*(\.\d{0,2})?$`; money `PreviewTextInput`+paste filters. **T5b/5c** `EmailPattern`, `IsValidPhone` (regex `^\+?[\d\s\-().]+$` + 7-15 digit count), `PhoneErrorText`/`EmailErrorText` red inline blocks, `LostFocus` validation + block in `TryValidateForSave`; keys `OrderEdit.Validate.EmailInvalid`/`PhoneInvalid` both blocks. **T5d** `MeasurementInputPattern` `^(\d+(\.\d*)?[+-]?)?$` on all 8 measurement boxes (`PreviewTextInput`+paste). SonarLint clean (reworded 2 comments to dodge S125 false positives); build succeeded 0 errors.

### 2026-07-23 — Simplify custom-made record summary  [DONE]
- Ask: "定制记录那一块，精简一下，不需要把所有测量的数据写出来，只需要大致的项目就行。" (Custom-made records: simplify — don't spell out all measurement numbers, just the rough items.)
- Plan:
  - [x] `CustomMadeRecordSummaryConverter`: replace measurement values with just the garment section names present (Jacket/Shirt)
  - [x] SonarLint clean, then build
- Notes: One converter drives edit list, detail panel, and receipt — summary now reads `Customer | Mode | AgeType | 上衣, 衬衫` (no measurement numbers). New `SectionName` helper returns the localized section label only when that garment has any measurement. SonarLint clean; build succeeded 0 errors.

### 2026-07-23 — Detail panel: show per-service unit/item prices  [DONE]
- Ask: "订单明细那块，每一项服务的单价和项目价格并没有给出。请添加" (Order-detail panel: unit price and item price for each service are not shown; please add them.)
- Plan:
  - [x] Alterations: show Subtotal + section Total
  - [x] Ready-made: add section Subtotal + Total (per-item price already present)
  - [x] Custom-made: show per-record price + section Total
  - [x] Build + SonarLint clean; update context.md
- Notes: `MainWindow.xaml` right-hand detail panel (`Detail.OrderItems`). Reused `CurrencyAmountConverter` (MultiBinding amount + `SelectedOrder.CurrencyType`; handles null → 0.00) and labels `Order.Fields.Subtotal` + `Receipt.SectionTotal`. Custom-made per-record money uses `SumTotal` with `RelativeSource AncestorType=Window` for currency (item DataContext is the record). No new localization keys. Build succeeded 0 errors.

### 2026-07-23 — Lock finalized orders + status filter + receipt price detail  [DONE]
- Ask: "1. if the order is completed or returned or canceled, once it saved, the order record shouldn't be edited anymore. 2. Add a filter to display order by status 3. For Receipt section on the main app, show price and subtotal price for each service and service items's prices."
- Plan:
  - [x] 1. Prevent editing of orders whose saved status is Completed/Returned/Cancelled (read-only edit window)
  - [x] 2. Add a status filter on the main window order list
  - [x] 3. Receipt section: show per-service price + subtotal and per-item prices
  - [x] Build + SonarLint clean; update context.md
- Notes: (1) `OrderEditWindow.xaml` named `FormRoot`/`SaveButton` + added `ReadOnlyNotice`; edit ctor inlines read-only when `existing.Status` is Completed/Cancelled/Returned (disable `FormRoot`, hide Save, show notice). Inlined instead of a helper to avoid S2325 false positive (SonarLint can't see XAML-generated fields in standalone analysis). (2) `MainViewModel` added `StatusFilter` (OrderStatus?) + `StatusFilterOptions` (null=All); `RebuildOrdersView` filters by status; `MainWindow.xaml` filter Border got a status ComboBox using `OrderStatusToLocalizedTextConverter` (converter now returns `Filter.Status.All` for null). (3) `BuildReceiptDocument` now prints per-item unit price + line total, plus per-service Subtotal + Total (`Receipt.SectionTotal`) for Alterations/Ready-made/Custom-made. New Languages.xml keys (both blocks): `Receipt.SectionTotal`, `Filter.Status.Label`, `Filter.Status.All`, `OrderEdit.ReadOnlyNotice`. Build succeeded 0 errors, SonarLint clean.

### 2026-07-23 — "OrderEdit.PickedUp" quick-complete checkbox  [DONE]
- Ask: "add a new checkbox beside \"结清所有尾款\" called \"已取货\"，once the checkbox is selected，the order status should be completed automatically，for the above status dropdown, it becomes unchangeable, unless you untick the checkbox. meanwhile, in the dropdown, if you manually changed it to completed, then the checkbox should be ticked as well."
- Plan:
  - [x] Add `OrderEdit.PickedUp` label to both `Languages.xml` blocks
  - [x] Add `PickedUpCheck` beside `ClearAllBalancesCheck` in `OrderEditWindow.xaml`; wire `StatusBox.SelectionChanged`
  - [x] `OnPickedUpChanged` / `OnStatusChanged` / `SelectStatus` with `_syncingStatus` guard
  - [x] Build + SonarLint clean
- Notes: No new DB column (state == `OrderStatus.Completed`). Build succeeded, 0 errors, no Sonar issues.

### 2026-07-23 — Workspace-wide SonarQube cleanup  [DONE]
- Ask: "Fix the rest of the files for SONARQUBE" (+ boolean-literal cleanup on `OrderEditWindow.xaml.cs`).
- Notes: Fixes captured as reusable rules in SKILL.md §10. Build succeeded, 0 errors; remaining flags are documented false positives (see context.md).
