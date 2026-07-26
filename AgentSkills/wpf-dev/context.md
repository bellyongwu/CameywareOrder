# Context — LeeYongeOrdering

Running project state, recent decisions, and gotchas. Update as work proceeds.
Read this (with `TODO.md` and `Architecture.md`) before starting any task.

## Workspace

- Repo: `d:\Projects\LeeYongeOrdering` (moved from `c:\` — older TODO entries
  still quote the old path)
- App process name (kill before building): `LeeYongeOrdering`
- Build/verify command:
  ```powershell
  Get-Process -Name LeeYongeOrdering -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 400
  dotnet build LeeYongeOrdering.csproj -v quiet --nologo 2>&1 | Select-String -Pattern "error|Build succeeded|Build FAILED"
  ```
  Expect `Build succeeded. 0 Error(s)`.

## Recent decisions / state

- **"Order items", not money, decide whether a service takes part**: `PaymentSectionControls`
  carries `HasItems()` (custom-made records exist / clothing rows exist / for Alterations a
  non-empty price box, since it has no item list), `SectionTotal()` and `HasMissingPrice`
  (has items but total ≤ 0). Used by BOTH `ApplyClearAllToSection` and the 全部服务总金额
  breakdown, so the two agree. 结清所有尾款 now ticks **已收定金 as well as 尾款结清** on every
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
- **实收定金 / 实收尾款 only count after their checkbox**: `Order.ReceivedDownpayment` sums
  through `SectionReceivedDeposit(money, XxxDownpaymentCompleted)` and the editor mirrors it —
  a typed deposit is what the shop EXPECTS, not what it holds. 实收尾款 was already gated on
  `BalanceCleared`. Both model and editor were changed together so a saved order reports the
  same figures the editor showed.

- **Small-print breakdowns in the order editor**: two code-filled panels now explain the
  headline figures. `ServicesTotalBreakdownPanel` (under 全部服务总金额) lists one line per
  charged section with a parenthetical — Alterations → service category, CustomMade →
  measured garment names, ReadyMade → the item categories actually priced — built by
  `RefreshServicesTotalBreakdown`/`AddServiceTotalDetail`. In each section's final
  breakdown, `*DepositTaxLineText`/`*FinalTaxLineText` split 此服务总计税 into
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
  - UI: ONE 税率 box per section (user's choice over two side-by-side boxes). It edits the
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

- **Final balance inherits the deposit's payment method until explicitly chosen**:
  `OrderEditWindow.EffectiveFinalMethod(PaymentSectionControls)` resolves the final
  method as `explicit selection ?? deposit method` (`None` never inherits). It is used by
  all 3 `Refresh*Totals` AND by `ApplyPaymentFields` on save, so persisted and on-screen
  amounts stay identical. WHY: `CardUsed` (= deposit card OR final card) drives the tax-rate
  display, but `Order.CalculateSectionPayment` taxes each portion by *its own* method — so
  picking Card for the deposit advertised 13% while the untouched (null) final method left
  the whole outstanding balance untaxed (entering a 124 price showed 税后总价 124 instead of
  140.12). The calculation engine itself was NOT changed.
  - The 当前计税 row (`*DepositTaxText`) now shows the section's whole tax (`money.Tax`),
    not just the deposit's tax, so it pairs with the 税后总价 line under it. English value of
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
  已退款或部分退款 (`Payment.Status.Refunded`) for cancelled/returned orders. Main list:
  `IsRefunded` rows are the lightest gray (#C3C9CF / opacity 0.5); `IsPickedUp`
  (completed/shipped) rows stay a bit darker (#9AA3AB / 0.7). Receipt totals colour the
  balance status (green / light green / orange / red via `ReceiptStatusLine` +
  `BalanceStatusBrush`) and OMIT the 剩余尾款 line when `IsRefunded`. In OrderEditWindow,
  switching the status to 已取消/已退货 dynamically locks every service/payment control
  (incl. 当前服务尾款已结清) via `SetServiceControlsEnabled(false)`, marks all
  checkboxes (incl. 已取货) with the `NotApplicableCheckBox` style (red box + red
  strikethrough label + red line across the whole control), and shows the refunded
  balance status; customer fields + the custom-made records list stay usable so
  measurements remain viewable. Reverting the status unlocks and re-runs
  `RefreshComputedTotals`. `_isRefunded` also participates in `RefreshPricingLocks` and
  gates PickedUp enabling. Balance status is computed — no DB change.
  - Gotcha: keep `RefreshPaymentSummary` cognitive complexity ≤15 — the balance-status
    text/colour block was extracted into `UpdateBalanceStatusDisplay`.

- **Custom-service (定制服务) list flag + measurement printing**: the main list
  dropped the Last Modified column (moved into the detail panel; ordering still
  defaults to LastModifiedDate desc in `LoadOrdersAsync`) and gained a centered,
  wrappable **定制服务** column driven by `Converters/CustomMadeServiceFlagConverter`
  (binds the whole `Order`; ConverterParameter `Flag`→有/无, `Names`→bracketed
  garment names with a zh 、 / en ", " separator, `NamesVisibility`). Order/Number
  and Balance-Status columns were widened (150→200, 140→180). `Order.
  HasCustomMadeService` `[NotMapped]` (any custom-made record with a garment
  carrying a cm/inch value) gates two new print actions on both the Print toolbar
  submenu and the row context menu: **打印量身尺寸** (measurements only) and
  **打印小票和所有尺寸** (receipt + measurements). Both open `Views/
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
  launched from 本地配置 → 测量术语; alt-language popup = `Views/
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
  labels + radios, and a divider at the top of the `FinalBlock` so deposit (定金) and
  final (尾款) read as two steps. All `x:Name`/handlers were preserved — restyle only.
- **Currency is a global app setting (not per-order)**: `Services/CurrencySettingService.cs`
  (singleton `Instance`, INotifyPropertyChanged) owns the chosen `CurrencyType` and its
  `Symbol` (￥ for CNY else $), persisted to `currency-setting.json` under LocalAppData.
  Edited via `Views/CurrencySettingWindow` launched from a `货币设置` item under 本地配置.
  `CurrencyAmountConverter` / `OrderPaymentSummaryConverter` / receipt / `OrderEditWindow`
  all read `CurrencySettingService.Instance.Symbol`. The per-order `Orders.CurrencyType`
  column is retained but unused (no migration); the old currency ComboBox + detail row
  were removed. Views refresh on next order load after a currency change.
- **Toolbar → 本地配置 menu**: the standalone 页眉页脚 button and the three database
  buttons were consolidated into a WPF `Menu` on the `MainWindow` toolbar. Top-level
  `本地配置` (`Toolbar.LocalConfig`) auto-expands to `添加或更改页眉页脚`
  (reworded `Toolbar.HeaderFooter`, still → `OnEditBrandingClick`) and a nested
  `本地数据库` (`Toolbar.LocalDatabase`) submenu holding 复制数据库路径 / 定位数据库文件 /
  打开数据目录 (reused `OnCopyDataPathClick` / `OnRevealDataFileClick` /
  `OnOpenDataFolderClick`). XAML + string-table only; no code-behind changes.
- **Per-portion payment tax (定金/实收定金, 尾款/实收尾款)**: tax now attaches to each
  payment portion only when THAT portion is paid by card (generalizes the old "any card
  taxes the whole section"). Single source of truth: `Order.CalculateSectionPayment(
  subtotal, deposit, ratePercent, downMethod, finalMethod)` → `SectionPayment` struct
  (Subtotal, Deposit, FinalBase=subtotal−deposit, ReceivedDownpayment, FinalCharge,
  Total, Tax); deposit is PRE-TAX and clamped to subtotal. Model section props delegate
  to `AlterationMoney`/`ClothingMoney`/`CustomMadeMoney`; new `Order.ReceivedDownpayment`
  (实收定金); `FinalBalance`/`ReceivedFinalBalance` use the taxed `FinalCharge`; section
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
  `logo.*` file under `%LocalAppData%\LeeYongeOrdering\Branding`;
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
  styled full-width "进入系统 / Enter System" button (`LanguageSelection.Enter`).
  Selecting a radio calls `SetLanguage` immediately so the panel text previews the
  chosen language live.
## Recent decisions / state

- **Custom-made record opens read-only when its section balance is cleared**:
  `OrderEditWindow.OnEditCustomMadeRecordClick` gates on
  `recordReadOnly = _isReadOnly || CustomMadeBalanceClearedCheck.IsChecked is true`
  (the same condition `RefreshPricingLocks` uses to lock the section's pricing).
  When true, `CustomMadeServiceWindow` is opened with `isReadOnly: true`, so its
  existing `ApplyReadOnlyMode` retitles to `OrderEdit.ViewCustomMade`
  ("查看定制记录"), makes every box/radio read-only, hides Save, and — via
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
  Because the "已取货 / picked up" tick is derived from `Status == Completed`
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
  - Modes: 只量身 / 定制量身 (Measure Only / Full Custom); **Full Custom is the
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
- **"已取货 / Picked up" quick-complete** added to `OrderEditWindow`:
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
  The dropdown **defaults to the first option** (服装修改/Garment Adjustments):
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
    `ConvertMeasurement` — only the leading number is converted (÷/×2.54,
    rounded to 2), the optional trailing `+`/`-` is preserved
    (`MeasurementNumberPattern = ^(\d+(?:\.\d*)?)([+-]?)$`). **Storage stays
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

## Gotchas

- Only edit the root `Languages.xml`; copies under `bin/`, `publish/` are build
  outputs.
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
