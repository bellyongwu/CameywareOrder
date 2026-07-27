# Architecture — CameywareOrder (WPF Ordering App)

Component map of the app this skill maintains. Keep this current whenever
components are added/renamed or the way pieces fit together changes.

## Stack

- **UI:** WPF, `net8.0-windows`, C# with `Nullable` + `ImplicitUsings` enabled.
- **Persistence:** EF Core 8 + SQLite. Schema is evolved at startup with
  idempotent runtime column guards in addition to the initial migration.
- **API (in-process):** Hot Chocolate GraphQL server hosted via the .NET
  Generic Host, preferring `http://localhost:5050` (scheme/host/port composed
  from constants in `App.xaml.cs`). The port is a **preference, not a
  requirement**: `ResolveServerPort` falls back to a free port when 5050 is
  taken, and `StartApiServerAsync` runs the app without the API if the bind
  still fails. Nothing in the UI consumes the endpoint — it is for external
  callers — so it must never block startup. `App.ApiEndpoint` holds the address
  actually bound.
- **PDF/print:** FlowDocument + `PrintDialog`; QuestPDF used for measurement
  export in the custom-made window.

## Startup / composition root

- `App.xaml` / `App.xaml.cs` — builds the Generic Host, registers
  `AppDbContext`, the GraphQL server, and view-models; runs startup schema
  guards (`EnsureDatabaseCompatibilityAsync` — data-driven `OrderColumnMigrations`
  table + `ReadOrdersSchemaAsync` / `TableExistsAsync` / `ReadColumnNamesAsync`
  helpers); loads the saved language via `LanguagePreferenceStore`.

## Layers / folders

- **Data/**
  - `AppDbContext` — `DbSet<Order> Orders`, `DbSet<OrderItem> OrderItems`
    (auto-property form); `OnModelCreating` maps precision, max-lengths,
    relationships, and `Ignore`s computed members.
  - `AppDbContextFactory` — `IDesignTimeDbContextFactory<AppDbContext>` for EF
    tooling; also writes a legacy migrations-history baseline.
  - `DatabasePathProvider` — resolves DB file path / connection string, ensures
    the folder exists; also owns the database **export/import** used by the
    导入/导出 menu: `ExportDatabaseTo` writes a **zip package** (`orders.db` +
    `-wal`/`-shm` sidecars + the whole `Documents/` tree, so attached images
    migrate with the data), and `ImportDatabaseFrom` restores it (zip-slip
    guarded via `ExtractPackageSafely`, falls back to the legacy raw-`.db` copy
    for older exports, backs up the current db + Documents folder first).
- **Services/**
  - `DocumentStorageService` — static global helper for custom-made record
    images: import/export/delete under
    `AppData\CameywareOrder\Documents\CustomMade`. NOTE: `using Path =
    System.IO.Path;` alias is required because `ImplicitUsings` pulls in
    `HotChocolate.Path`, making bare `Path` ambiguous.
  - `MeasurementTermsService` — singleton `Instance` for the Measurement Terms
    system; persists `measurement-terms.json` under LocalAppData. Holds the
    `MeasurementTermsConfig` (terms + garments + garment→term maps); resolves
    localized names (predefined → `Measure.Term.*`/`Measure.Garment.*` string
    table, custom → per-language Names dict); add/edit/delete custom terms &
    garments (with a `MeasurementGender` classification + `IsDuplicateTermName`
    guard); add/remove term↔garment mapping (blocks locked predefined pairs
    unless the garment has `EnableCustomMeasurements`, undone by
    `RestoreDefaultMeasurements`); `LoadOrSeed`+`MergePredefined` seed/upgrade;
    `ExportConfigJson` / `TryParseConfigJson` / `ImportConfig` back the
    量身项目设置 import/export; `ConfigChanged` event.
  - `CurrencySettingService` — singleton `Instance` (`INotifyPropertyChanged`)
    owning the **global** currency (`Current` + `Symbol`: ￥ for CNY else $),
    persisted to `currency-setting.json` under LocalAppData. Currency is an app
    setting, not per-order — the `Orders.CurrencyType` column is retained but
    unused.
  - `ReceiptBrandingStore` — static store for the receipt/measurement branding:
    `receipt-branding.json` + a `logo.*` file under
    `%LocalAppData%\CameywareOrder\Branding`. `ReceiptBrandingSettings` holds
    per-language `LocalizedBranding` (`HeaderXaml`/`FooterXaml`) plus
    `LogoFileName` + `LogoPlacement` (Left/Center/Right, default Center).
    `ExportConfigJson` / `TryParseConfigJson` / `ImportConfig` (+ `BrandingExport`
    DTO) make the 页眉页脚 export **self-contained** — the logo travels as base64
    inside the JSON.
  - `GlobalSettingsPackage` — static one-file backup of everything held locally: a zip
    with `settings.json` (currency, language code, `MeasurementTermsConfig`,
    `BrandingExport`, version + timestamp) plus a **nested** `database.zip` produced by
    `DatabasePathProvider.ExportDatabaseTo`. `ExportTo` / `TryRead` (validates with no
    side effects) / `Import` (applies only the sections present; database first, since it
    is the one destructive step and the one that self-backs-up). Backs the 全局设置
    entry in the 导入/导出 menu.
  - `BrandingRenderer` — static renderer that round-trips branding content
    between a `RichTextBox` FlowDocument and its XAML string
    (`XamlWriter.Save` / `XamlReader.Parse`), appends it to a printed receipt
    (`AppendToFlowDocument`, `CreateLogoBlock`), and renders the same XAML into
    QuestPDF spans for the measurements PDF (`RenderToPdf`, `AlignLogo`).
    `IsEmpty(headerXaml)` is the gate that decides whether the built-in document
    title is printed.
  - `CustomMadeMeasurementReader` — static read-only helper that projects an
    order's saved `CustomMadeRecords` into print/UI shapes: `GetGarmentNames`
    (distinct, order-preserving garment display names in a given language) and
    `BuildSections` (per garment: name + term/value rows in the requested unit,
    ordered by the garment's configured term order; per-garment work factored
    into `BuildGarmentSection`). Resolves names via `MeasurementTermsService`;
    used by the 定制服务 list column and the measurement print paths.
- **Models/**
  - `Order` — customer + per-section (Alteration / CustomMade / Clothing) money
    fields, **a payment method per portion** (deposit + final balance), **a tax rate
    per portion** (`XxxTaxRate` = deposit stage, `XxxFinalTaxRate` = final stage;
    a null final rate means a pre-split order whose single rate applies to both),
    cleared flags, status; many `[NotMapped]` computed totals/residuals. Money is
    derived through the static `Order.CalculateSectionPayment(...)` → `SectionPayment`
    record struct (per-**portion** tax: a portion is taxed only when its own
    method is Card, at its own rate; deposit is pre-tax and clamped to subtotal). Per-section
    `XxxMoney` accessors feed `XxxTotal`/`XxxTax`, `ReceivedDownpayment` (实收定金),
    `TotalTax`, `FinalBalance` (剩余尾款), `ReceivedFinalBalance` (实收尾款), and
    the `IsSectionCleared`/`SectionResidual`/`SectionReceivedFinal` helpers.
    Per-section `XxxAddedToReceipt` gates (`total > 0 && deposit method selected`)
    are shared by the receipt and detail panel; `Items` collection. The
    `HasCustomMadeService` `[NotMapped]` gate (any custom-made record with a
    garment carrying a cm/inch value) drives the 定制服务 list flag and gates the
    measurement print actions. `IsRefunded` (Status Cancelled/Returned) +
    `PaymentStatusKind` (`BalanceStatusKind` enum: Outstanding / ClearedPickedUp /
    ClearedNotPickedUp / Refunded) are the single source of truth for the
    balance-status indicator (label + colour) across the list, detail panel and
    receipt; `IsPickedUp` covers **Shipped or Completed**. Cancel/return reason is
    stored as a pair: `StatusReasonCategory` (stable key — CustomerDoesNotWant /
    ServiceUnsatisfactory / ProductIssue / PriceTooHigh / Other) plus
    `StatusReason` (free text, only meaningful for `Other`).
  - `SectionPayment` — immutable `readonly record struct`
    (Subtotal, Deposit, FinalBase, ReceivedDownpayment, FinalCharge, Total, Tax)
    holding one section's money split.
  - `OrderItem` — clothing line item (`UnitPrice`, `PromotionalPrice`, computed
    `EffectiveUnitPrice` / `TotalPrice`).
  - `CustomMadeServiceRecord` — measurement record (serialized to
    `Order.CustomMadeRecordsJson`); carries a `Documents` list of
    `CustomMadeDocument` references and a garment-driven `Garments` list
    (`GarmentMeasurement` → `MeasurementValue` cm/in pairs keyed by garment/term
    id). Legacy static Jacket*/Shirt* fields are retained for back-compat and
    migrate into `Garments` on next save.
  - `MeasurementTerm` / `GarmentType` / `MeasurementTermsConfig` /
    `MeasurementTermDefaults` (`Models/MeasurementTerm.cs`) — the Measurement
    Terms domain: a term has an id + `IsPredefined` + per-language `Names` + a
    `MeasurementGender` (Common/Male/Female, used to filter the "all measurements"
    list; predefined classifications come from
    `MeasurementTermDefaults.GetPredefinedTermGender`); a garment has an id +
    `IsPredefined` + `Names` + ordered `TermIds` + `UseCustomMeasurements` (when
    set, a predefined garment's locked default mapping is released so any term may
    be added/removed). Defaults seed 21 predefined term ids and 7 predefined locked
    garments (jacket/vest/shirt/pants/blouse/dress/qipao) with default garment→term
    maps; `IsTermLockedInGarment` enforces predefined pairs.
  - `CustomMadeDocument` — reference to an uploaded image attached to a
    custom-made record (`Category` enum: HandwritingReceipt/Fabric/Photo/Other,
    `FileName`, `StoredFileName`). Image bytes live on disk in the document
    store; only this reference is serialized into the record JSON.
- **GraphQL/**
  - `Query` — `GetOrders`, `GetOrderAsync`.
  - `Mutation` — create/update/delete order, add/remove order item.
- **Localization/**
  - `LocalizationService` — singleton `Instance`, indexer `["Key"]`, `Format`,
    `LanguageChanged` event; reads `Languages.xml`.
  - `LanguagePreferenceStore` — persists the chosen language code.
- **Converters/** — `CurrencyAmountConverter`, `LocalizationLookupConverter`,
  `NullToVisibilityConverter`, `OrderStatusToLocalizedTextConverter`,
  `CustomMadeRecordSummaryConverter`, `OrderPaymentSummaryConverter`,
  `PositiveAmountToVisibilityConverter`, `CustomMadeServiceFlagConverter`
  (binds the whole `Order` row; ConverterParameter `Flag` → localized 有/无,
  `Names` → bracketed garment names with a zh 、 / en ", " separator,
  `NamesVisibility` → Visible/Collapsed), `BalanceStatusColorConverter`
  (`Order.PaymentStatusKind` → brush: green #2E7D32 / light green #66BB6A /
  orange #EF6C00 / red #C62828), `OrderServicesSummaryConverter` (lists every
  service actually present in the order, not just the stored primary
  `ServiceType`), `ReturnReasonSummaryConverter` (`IMultiValueConverter` over
  category + free text; its `public static Resolve(category, freeText)` is reused
  by the receipt code-behind), `DocumentThumbnailConverter` (stored file name →
  128px `BitmapImage`, decoded `OnLoad` so the file is never locked), and
  `LastItemBorderThicknessConverter` (last-item border-thickness
  `IMultiValueConverter`). The built-in `BooleanToVisibilityConverter` is also
  registered in `MainWindow.xaml` (`BoolToVisibility`) for the section gates.
- **ViewModels/**
  - `MainViewModel` — order list, paging, search, delete, **copy order**
    (`CopyOrderCommand`/`CopyOrderAsync`: deep-copy an aggregate, reset a closed
    status to `Processing`); **column sorting** (`SortBy(key)` + `SortKey`/
    `SortAscending` state + `GetSortSelector`, applied over the whole filtered set
    before paging in `RebuildOrdersView`); `DatabaseFilePath` (WPF-bound, kept
    instance).
  - `RelayCommand` — `ICommand` helper.
- **Views/**
  - `MainWindow` — order list + detail + paging. The list is a **`ListView` +
    `GridView`** (not a DataGrid) with a right-click `ListView.ContextMenu`
    (Edit/Copy/Delete/Print) and a `PreviewMouseRightButtonDown` row-select
    `EventSetter`, keyboard shortcuts (`Enter` = open/details, `Delete` = delete
    command), and **clickable column headers that sort** (asc/desc toggle + ▲/▼
    glyph) via the `GridViewColumnHeader.Click` handler and the
    `OrderColumnSort` attached properties. The Edit toolbar button + context-menu
    item relabel to "查看订单 / View Order" for read-only orders
    (`RefreshToolbarLabels`). The list also shows a **left-aligned**, wrappable
    **定制服务** column (via `CustomMadeServiceFlagConverter`: 有/无 + bracketed
    garment names; cell panel `Stretch`, both `TextBlock`s `Left`, so the flag and
    the wrapped names share one left edge);
    the former Last Modified column moved into the detail panel (ordering still
    defaults to LastModifiedDate desc in `LoadOrdersAsync`). Rows gray out by
    status: **Cancelled/Returned** (`IsRefunded`) are the lightest gray,
    **Completed/Shipped** (`IsPickedUp`) a bit darker. When
    `SelectedOrder.HasCustomMadeService` is true, the Print toolbar submenu and the
    row context menu expose **打印量身尺寸** (measurements only) and
    **打印小票和所有尺寸** (receipt + measurements); both open
    `MeasurementPrintOptionsWindow` then print via `PrintDialog` + `FlowDocument`
    (`PrintMeasurements`/`BuildMeasurementDocument`/`AddMeasurementSections`, the
    latter starting on a fresh page when appended after a receipt). Measurement
    language/unit come from the dialog; the receipt portion stays in the UI
    language. Detail-panel service sections are shown/hidden via
    the `Order.XxxAddedToReceipt` gates, and show the 定金/实收定金 and
    剩余尾款/实收尾款 pairs.
    The toolbar carries a `本地配置` (`Toolbar.LocalConfig`) `Menu` holding
    添加或更改页眉页脚, 货币设置, 测量术语, a `本地数据库` submenu (copy path / reveal
    file / open folder) and a **导入/导出** (`Toolbar.ImportExport`) submenu with
    Export+Import pairs, in order: `Toolbar.HeaderFooter` (JSON + base64 logo via
    `ReceiptBrandingStore`), `Toolbar.MeasurementTerms` (JSON via
    `MeasurementTermsService`), `Toolbar.LocalDatabase` (zip package via
    `DatabasePathProvider`), then a separator and `Toolbar.GlobalSettings`
    (everything at once via `GlobalSettingsPackage`). Every import confirms with a
    Yes/No warning dialog first and reports through `MainViewModel.StatusMessage`;
    export file names get a date suffix via `BuildDatedExportFileName`.
  - `OrderColumnSort` (static, in `MainWindow.xaml.cs`) — attached properties
    `SortKey` (per-column sort member) and `SortGlyph` (header arrow), consumed by
    the header `ContentTemplate` and `UpdateSortGlyphs`.
  - `OrderEditWindow` — the large create/edit form: per-section pricing &
    payment, a **stage-aware tax-rate box** (one box per section that edits the
    deposit rate until the deposit is marked received and the final-balance rate
    afterwards, with a label naming the stage — `PaymentSectionControls` holds both
    rates plus `ShowingFinalRate`/`IsFinalStage`, resolved by `ApplyStageTaxRates` /
    `ResolveStageRate` and seeded by `LoadStageTaxRates`), computed summary,
    "clear all balances" master checkbox, and the
    "已取货 / Picked up" quick-complete checkbox that locks the status dropdown.
    Switching the status to 已取消/已退货 puts the editor in a **refund lock**
    state (`_isRefunded`): every service/payment control (incl. 当前服务尾款已结清)
    is disabled via `SetServiceControlsEnabled(false)`, all checkboxes (incl.
    已取货) get the `NotApplicableCheckBox` style (red box + red strikethrough
    label + red line across the whole control), and 余额状态 shows
    已退款或部分退款; customer fields + the custom-made records list stay usable so
    measurements remain viewable. Reverting the status unlocks and re-runs
    `RefreshComputedTotals`. `_isRefunded` also feeds `RefreshPricingLocks`,
    `UpdateBalanceStatusDisplay` and the PickedUp enable rule.
  - `CustomMadeServiceWindow` — measurement capture + PDF export, plus a
    **Documents** section (4 categories: handwriting receipts / fabrics / photos
    / others; multiple images each) with Upload/View/Download/Replace/Delete.
    Uploads copy into the store immediately but commit only on Save (rolled back
    in `OnClosed` when not saved). Edit buttons bind `IsEnabled` to the window's
    `CanEditDocuments` (`!_isReadOnly`) via `RelativeSource AncestorType=Window`.
    Measurements are **garment-driven**: a `ToggleButton` selector (from
    `MeasurementTermsService.Instance.Garments`) preselects garments and only the
    related terms render as dynamically-generated per-garment measurement cards.
    A cm/in dual-unit cache (`_valueCache` garmentId→termId→cm/in +
    `_termEditors`) is the session source of truth; unit switch and language
    change persist then rebuild; old records seed from legacy Jacket/Shirt fields;
    `BuildGarmentsIntoRecord` writes `record.Garments` on save; PDF export
    iterates all selected garments/props (`BuildPdfGarmentSections`).
  - `MeasurementTermsWindow` — the 3-column drag-drop mapping UI for the
    Measurement Terms system (left = garments list with lock/alt-language/delete +
    Add Garment; center = assigned-terms drop zone; right = all terms as draggable
    chips + Add Measurement). Modern card styling + Segoe MDL2 icons; predefined
    term/garment names locked; custom items support inline edit/save/delete +
    alt-language remap. Launched from the 本地配置 menu.
  - `MeasurementTermLanguageWindow` — alt-language name editor popup (one name row
    per `LocalizationService.AvailableLanguages`); returns a langCode→name dict.
  - `MeasurementPrintOptionsWindow` — small pre-print dialog asking for the
    measurement **language** (radios from `LocalizationService.AvailableLanguages`,
    default = current) and **unit** (cm default / inch); exposes
    `SelectedLanguageCode` + `IsInch` (set on Print). Feeds the 打印量身尺寸 /
    打印小票和所有尺寸 print paths (a print method, not save-to-PDF).
  - `ReceiptBrandingWindow` — the 页眉页脚 rich-text editor: a logo card
    (choose/remove + Left/Center/Right placement radios), a formatting ribbon
    (B/I/U, font size, align, colour swatches), and one tab per language each
    holding a header + footer `RichTextBox`. Persists via `ReceiptBrandingStore`;
    content is injected into the printed receipt and the measurements PDF by
    `BrandingRenderer`.
  - `CurrencySettingWindow` — small 货币设置 dialog (currency ComboBox +
    Save/Cancel) writing through `CurrencySettingService`.
  - `DocumentPreviewWindow` — in-app image viewer (loads via `BitmapImage`
    `OnLoad` so the file is not locked).
  - `LanguageSelectionWindow` — first-run language picker.
- **Migrations/** — `InitialCreate`, `AddOrderPaymentFields`, and the model
  snapshot. Columns added after those two migrations arrive through the runtime
  guards in `App.xaml.cs` instead (see Startup above).
- **Languages.xml** (project root) — the single source string table (Chinese
  block first, English block second).

## Key cross-cutting patterns

- All UI text flows through `Languages.xml` / `LocalizationService`.
- Per-section money math is centralized in `Order.CalculateSectionPayment` and
  reused by the model and the live editor summary so persisted and on-screen
  values match; tax is applied **per payment portion** (deposit vs. final) based
  on that portion's method **and its own rate**. The editor persists whatever it
  displayed — both stage rates, and the final method resolved through
  `EffectiveFinalMethod` — so a reloaded order never recomputes to different
  amounts than the ones the shop saw when saving.
- The paged order list is sorted in `MainViewModel` over the whole filtered set
  before `Skip/Take`, driven by per-column `OrderColumnSort.SortKey` attached
  properties (never `Items.SortDescriptions`, which would sort one page only).
- Control-sync handlers use reentrancy guard flags (`_syncingPayment`,
  `_syncingStatus`) to avoid event loops.
- Order "picked up" state is represented purely by `OrderStatus.Completed`
  (no separate column); the "已取货" checkbox is only enabled once the order has a
  charge and every final balance is cleared, and read-only statuses relabel the
  open action to "查看订单 / View Order". **Read-only statuses are
  Completed / Shipped / Cancelled / Returned** — kept in sync across three
  `IsReadOnlyStatus`-style predicates (`MainWindow.xaml.cs` label,
  `OrderEditWindow.xaml.cs` `_isReadOnly`, and `MainViewModel.IsClosedStatus`
  which resets a copied order back to `Processing`); change all three together.
- Anything with an Import/Export must export **self-contained**, so the whole app
  can be migrated to another PC: base64-in-JSON for a single small asset (the
  branding logo), a bundled zip package for many/large files (the database plus
  its `Documents/` image tree). Imports confirm before overwriting and back up
  what they replace.
- A service section is "added" only when it carries a charge **and** a deposit
  method is selected; the `Order.XxxAddedToReceipt` gate is the single source of
  truth for both the printed receipt and the on-screen detail panel.
- Balance status is derived, never stored: `Order.PaymentStatusKind` /
  `IsRefunded` are computed from the order's money + `Status`, and each surface
  (list column, detail panel, receipt, editor) maps that to its own label +
  colour (green / light green / orange / red, via `BalanceStatusColorConverter` /
  `BalanceStatusBrush`). Cancelled/returned orders are "refunded": they show
  已退款或部分退款, and on both the receipt and the detail panel the
  **收款明细 / payment-method breakdown is replaced by the 取消原因/退货原因**
  (`ReturnReasonSummaryConverter`) — the charge lines, totals and 剩余尾款 still
  print, so a refunded receipt keeps full parity with a normal one.
- Destructive actions (delete) own their confirm dialog inside the command, so
  toolbar, context menu, and the `Delete` key share one prompt.
