# Architecture — CameywareOrder (WPF Ordering App)

Component map of the app this skill maintains. Keep this current whenever
components are added/renamed or the way pieces fit together changes.

## Stack

- **Languages:** shipped per file under `Settings/System/Languages` and DISCOVERED, but a shop
  installs a subset — see `Services/ShopLanguages`, which is what every language picker in the app
  resolves through.
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
  Session flow: sign in → `OpenShopOrSignInAgainAsync` → main window.
  `SignInAsAsync(userName)` is the administrator's "sign in as this user": structurally a sign-out
  that skips the login window — main window down, session swapped, shop picker re-run. Reached from
  `MainWindow`; from the shop picker the same switch arrives as `ShopSelection.UserSwitched`, a
  THIRD outcome that makes the loop round again as the new user rather than signing them out.
  That method LOOPS — cancelling the shop picker signs the user out and shows
  sign-in again rather than ending the application, because the two steps read as
  one flow and Cancel on the second means "go back". `Shutdown()` is reached only
  when the LOGIN window is dismissed. `SignOutAsync` reuses the same loop, so the
  startup and sign-out paths cannot drift; it deliberately keeps the previous
  session's shop bound until the next is chosen, since the running GraphQL server
  calls `ShopContext.RequireCurrent`.

## How the source is filed (v5.0.0)

`Views/`, `Models/` and `Services/` each split into the **same three folders**, so one question —
"whose is this?" — answers where a file lives in all three:

- **`UserManagement/`** — people: accounts, roles, sessions, the roster.
- **`StoreManagement/`** — a shop and everything it does: its settings, its catalogue and tax rules,
  and the ORDERS it takes (the order and custom-made screens are a shop's daily work, not chrome).
- **`Global/`** — what belongs to no single shop and no single person: the confirmation dialog, the
  image viewer, the first-run language picker, the installation-wide currency setting, the data
  folder and its migrations, contact validation, money rounding.

**The namespaces did NOT change.** Everything under `Views/` is still `CameywareOrder.Views`, and
likewise for Models and Services. The folders are for a reader; renaming the namespaces would have
touched every `using`, every `x:Class` and every `xmlns:` in the markup for no gain, and nothing in
the application references a source path (checked before moving: no pack URI, no MergedDictionary
and no csproj item names one). The two `Themes/` dictionaries ARE referenced by path — those did not
move.

## Layers / folders

- **Data/**
  - `AppDbContext` — `DbSet<Order> Orders`, `DbSet<OrderItem> OrderItems`
    (auto-property form); `OnModelCreating` maps precision, max-lengths,
    relationships, and `Ignore`s computed members.
  - `AppDbContextFactory` — `IDesignTimeDbContextFactory<AppDbContext>` for EF
    tooling; also writes a legacy migrations-history baseline.
  - `DatabasePathProvider` — resolves DB file path / connection string, ensures
    the folder exists; also owns the database **export/import** used by the
    Import/Export menu: `ExportDatabaseTo` writes a **zip package** (`orders.db` +
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
    Measurement Terms import/export; `ConfigChanged` event.
  - `ShopAdministration` (static) — the one place the shop-level destructive rules live: `Delist` /
    `Activate` (sets the EXISTING `Shop.IsArchived`, which the startup load, the picker and the
    name-uniqueness check already honour, plus a `DelistedOnUtc` audit stamp), `Delete` (orders, items,
    the shop row, and the per-shop FILES named after its `PublicId`), `Reinitialize` (every shop —
    accounts, language and global settings deliberately kept, so nobody is locked out), `CountOrders`,
    `AllShops`, and `CreateDemoShop` (one click, built from the shipped presets, no fabricated orders).
    Every read of `Orders` here says `IgnoreQueryFilters()`: the context confines Orders to the OPEN
    shop, so a cross-shop delete through a normal query silently matches nothing.
  - `ShopArchive` (static) — SELECTED shops in and out of one zip: the "download all data" export and
    the file a restore reads. Deliberately not `DatabasePathProvider.ExportDatabaseTo`, which packages
    the whole database file and whose import REPLACES it — restoring one deleted shop would take every
    other shop with it. Works in rows, so an export is a subset and an import is additive: a shop whose
    `PublicId` is already present is SKIPPED (not merged, not duplicated — duplicating would leave two
    shops sharing one per-shop file name) and the count is reported so the panel can say so. `TryRead`
    validates with no side effects, like `GlobalSettingsPackage.TryRead`. Wraps the restore in
    `AppDbContext.SuppressShopStamping()`.
  - `AuthenticationService` — singleton `Instance`; sign-in **and** authorization. Accounts live in
    `credentials.json` under LocalAppData (outside the database on purpose: an Import → Database restore replaces the
    whole database file and must not wipe the accounts). An account is either an administrator
    (`IsAdministrator` — everything, everywhere, never a shop membership) or holds `ShopMembership`s:
    **one record per shop**, carrying the role(s) held there, `IsActive`, `JoinedOn`, `DeactivatedOn`
    and a `TimeOnly?` shift, keyed on **`Shop.PublicId`**. `BindShop` supplies the shop the named
    capabilities resolve against: `CanCreateShops` / `CanManageUsers` / `CanUseDataTools` /
    `CanDeleteAccounts` (administrator, whole-installation) and `CanConfigureShop` /
    `CanManageStoreMembers` (administrator or the open shop's manager).
    `CanChooseAnyLanguage` (administrator only) means "any SHIPPED language" — NOT "may switch
    language at all", which is `ShopLanguages`' question now that a shop installs its own set.
    `RoleFor` / `CanAccessShop` / `FilterAccessibleShops` answer per shop and ignore deactivated
    memberships; `StrongestRole` takes the MINIMUM `UserRole` because the enum is ordered
    strongest-first. `Authenticate` returns a `SignInResult` whose `SignInFailure` distinguishes bad
    credentials from an account deactivated in EVERY shop it belongs to. Roster CRUD
    (`ListMembers` / `AddMember` / `UpdateMember` / `CanSetPasswordFor` → `AccountOperationResult`)
    backs Store Members; installation-wide CRUD (`CreateAccount` / `DeleteAccount` / `SetPassword` /
    `SetShopRoles` / `UpdateAccountContact`) backs User Management.
    A person's name is **`FirstName` + `LastName`** (schema 4; the old single `DisplayName` is
    split on load — see context.md for the rule and why it is conservative). `PersonName` composes
    them: `Full`, `Label` (name, or the login when there is none — never blank) and `Greeting` (the
    FIRST name, which is what the main window says Hi to). `UserAccount.HeldRoles()` is the distinct
    roles held across ACTIVE memberships, strongest first — a method, not a property, because it
    allocates.
    `UpdateAccountProfile` writes the account-level half — name, login and contact — as ONE
    validated operation, including a **rename**, which is refused for the administrator (a product
    rule) and for a name another account already holds. It deliberately does NOT touch
    `ProvisionedAccounts`: that list records which SEED NAMES have been created, so the old name
    staying in it is what stops the next load re-seeding the original.
    `IsUserNameTaken` / `IsUserNameTakenByAnother` are public so the screens can report availability
    as a name is typed; the save path re-checks regardless.
    `SignInAs` hands the session to another account without its password — the administrator's
    "sign in as this user". Gated IN THE SERVICE (unlike the roster edits, which only write data):
    administrator only, never yourself, never an account delisted by every shop. Clears the bound
    shop, since capabilities must not go on resolving against the shop the administrator had open.
    `ProvisionSeedAccounts` identifies the administrator by its **flag**, not its name, so "exactly
    one administrator" holds structurally.
    `PhoneNumber` and `Email` are **account-level**, not per membership — one person
    working at two branches has one phone and one mailbox. Both nullable and stored
    null-when-blank (never `""`), so existing files need no migration.
    `UpdateAccountContact` exists because `UpdateMember` can only reach people who
    belong to a shop while `CreateAccount` deliberately makes accounts that belong to
    none; it touches no membership, so unlike a role change it is safe on the
    administrator and on one's own account. Validation is `ContactValidation`, shared
    with the order form. The administrator cannot be deleted or given memberships, and no
    account can be promoted to administrator. File schema version **4**: the version-2 fold (flat
    assignments → memberships) and the version-3 name split both run on load,
    `ApplyLegacyShopMemberships` completes the version-1 upgrade once shops are readable, and
    `ProvisionedAccounts` makes deleting a seeded account permanent.
  - `CurrencySettingService` — singleton `Instance` (`INotifyPropertyChanged`)
    owning the **global** currency (`Current` + `Symbol`: ￥ for CNY else $),
    persisted to `currency-setting.json` under LocalAppData. Currency is an app
    setting, not per-order — the `Orders.CurrencyType` column is retained but
    unused.
  - `ReceiptBrandingStore` — static store for the receipt/measurement branding:
    `receipt-branding.json` + a `logo.*` file under
    `%LocalAppData%\CameywareOrder\Branding`. `ReceiptBrandingSettings` holds
    per-language `LocalizedBranding` (`HeaderXaml`/`FooterXaml`) plus
    `LogoFileName` + `LogoPlacement` (Left/Center/Right, default Center) and the shop's
    `TaxRegistrationNumber` (NOT per language; printed directly under the
    receipt header, and edited under the Header card in the branding editor. What it is CALLED comes
    from the shop's tax jurisdiction — see `TaxJurisdiction.TaxNumberLabel`; "GST/HST" used to be
    spelled into the label and the receipt line in all five languages).
    `ResolveTaxRegistrationNumber(settings)` applies the override rule — the number
    typed into the header/footer editor wins over the shop's own, being the more
    specific surface. It lives here rather than in either printer because the
    receipt and the measurements PDF both print it, and two copies of an override
    rule drift apart.
    `ExportConfigJson` / `TryParseConfigJson` / `ImportConfig` (+ `BrandingExport`
    DTO) make the Header & Footer export **self-contained** — the logo travels as base64
    inside the JSON.
  - `GlobalSettingsPackage` — static one-file backup of everything held locally: a zip
    with `settings.json` (currency, language code, `MeasurementTermsConfig`,
    `BrandingExport`, version + timestamp) plus a **nested** `database.zip` produced by
    `DatabasePathProvider.ExportDatabaseTo`. `ExportTo` / `TryRead` (validates with no
    side effects) / `Import` (applies only the sections present; database first, since it
    is the one destructive step and the one that self-backs-up). Backs the Global Settings
    entry in the Import/Export menu.
  - `BrandingRenderer` — static renderer that round-trips branding content
    between a `RichTextBox` FlowDocument and its XAML string
    (`XamlWriter.Save` / `XamlReader.Parse`), appends it to a printed receipt
    (`AppendToFlowDocument`, `CreateLogoBlock`), and renders the same XAML into
    QuestPDF spans for the measurements PDF (`RenderToPdf`, `AlignLogo`).
    `IsEmpty(headerXaml)` is the gate that decides whether the built-in document
    title is printed. Parses with `XamlReader.Parse(xaml) as FlowDocument`, so
    branding whose root is not a `FlowDocument` renders as nothing, silently.
  - `OrderNumberFormatter` — static; builds a shop's order/receipt numbers from its configured
    format (`OrderNumberMode` Timestamp/Sequential/DailySequential/YearlySequential + prefix +
    padding). `Preview` (no reservation), `Reserve` (skips numbers already taken),
    `CommitSequence` (advances the counter, called only AFTER the order is saved so an abandoned
    form cannot burn a receipt number), `SequenceKeyFor` (the period a counter belongs to —
    empty for a continuous run, which therefore never restarts).
  - `CustomMadeMeasurementReader` — static read-only helper that projects an
    order's saved `CustomMadeRecords` into print/UI shapes: `GetGarmentNames`
    (distinct, order-preserving garment display names in a given language) and
    `BuildSections` (per garment: name + term/value rows in the requested unit,
    ordered by the garment's configured term order; per-garment work factored
    into `BuildGarmentSection`). Resolves names via `MeasurementTermsService`;
    used by the Custom Service list column and the measurement print paths.
  - `ShopLanguages` — static; the one answer to **which languages this session may pick
    from**. `Installed(shop)` is the set a branch runs in (never empty: a shop with
    nothing installed falls back to its `PreferredLanguageCode`, which reproduces the
    behaviour every shop had before the setting existed; one that has said nothing at
    all has restricted nothing and gets everything). `Selectable(shop, canChooseAnyLanguage)`
    narrows that by role — every shipped language for an administrator, the installed set
    for a manager or staff member. `PreferredCode(shop)` is the language a shop OPENS in:
    its preference when it installs it, otherwise the first language it does, because the
    two fields can disagree and a branch must never open in a language its own toggle
    cannot return to. `InstalledSummary(shop)` is the line under the greeting, and always
    describes the SHOP rather than an administrator's wider choice.
    Lives outside both `AuthenticationService` and `ShopContext` because the answer is a
    product of both. Consumed by `MainWindow`'s toggle, `ShopSetupWindow`,
    `MeasurementPrintOptionsWindow` and `CustomMadeServiceWindow`'s download panel.
  - `ShopCurrencies` — the one answer to "which currencies may an order in this shop be priced
    in". `Offered(localization)` derives the whole set from the INSTALLED languages: each
    `*.lang.xml` declares its market's currencies under `Currency.Codes`, English's lead (CAD before
    USD), and a declared code the `CurrencyType` enum cannot name is dropped. `ForLanguage(code)` is
    what the localization panel lists beside each language. Then `Supported(shop)` (never empty — a
    shop that has recorded none falls back to its own `CurrencyType`, the pre-feature behaviour;
    a currency it accepts but the offer no longer contains is kept, at the end), `Preferred`,
    `CanChoose`, `Offers`, `SymbolOf(order)`, `SupportedSummary`. Shaped like `ShopLanguages` on purpose, and differs from it twice:
    **no per-user capability** (an administrator sees every language because language is only how a
    screen reads; currency is a fact about the order, so pricing outside the shop's set would be a
    wrong number on a real receipt), and the set is bounded by the `CurrencyType` **enum** rather
    than a discovered folder. `Shop.SupportedCurrenciesJson` stores enum NAMES, never the integers.
    **`SymbolOf` reads the ORDER** — `CurrencySettingService` describes the shop today, which is a
    different question from what an order was priced in.
  - `ShopLetterhead` — the letterhead the application GENERATES when the
    header/footer editor has supplied none: `Name`, `Subtitle`, `ContactLines`
    (`ShopLetterheadLine` label+value), `TaxLine`. `Build(localization, languageCode,
    subtitleKey)` resolves every string for an explicitly passed language, because
    the measurements sheet is produced in the language chosen in the print dialog
    rather than the UI one. Plain strings, not blocks or spans, because both the
    FlowDocument printer and the QuestPDF exporter consume it.
    Its rules: the tax number is the **last** line; a custom header **replaces**
    this block rather than stacking on it; the document title is `Subtitle`, and
    moves into the body when a custom header replaces the letterhead. Used by the
    receipt (`AddReceiptTitle`), the printed measurements sheet
    (`AddMeasurementLetterhead`) and `MeasurementSheetDocument` alike — they had
    drifted, and the measurement paths printed a bare GST/HST line above the title
    while never naming the shop.
  - `MeasurementSheetDocument` — static; lays out the custom-made measurements
    PDF. `Compose(content)` returns the `IDocument`, `Save(content, path)` writes
    it. Takes `MeasurementSheetContent` (title, `MeasurementSheetRow` info rows,
    `MeasurementSheetSection` garment blocks, a `ShopLetterhead`, header/footer XAML, logo)
    — **plain, already-localized data with no string keys**, because the sheet is
    generated in the language chosen in the print dialog rather than the UI
    language, so the composer must not look anything up. Branding sits in the
    page's own `Header()`/`Footer()` slots so it repeats on every page; each
    garment's name is its table's repeating `Header` row.
    Lives outside `CustomMadeServiceWindow` (which now only gathers the data)
    because a window cannot be opened without a message loop, and a print layout
    checkable only by a human clicking Export is one whose regressions ship.
- **Models/**
  - `ContactValidation` — static; the one definition of a usable phone number and
    email address (`IsValidPhone`, `IsValidEmail`). Shared by the order form's
    customer fields and the roster's member fields; blank is VALID in both, since
    "required" is a separate question the caller answers. The rules were private to
    `OrderEditWindow`, and a second copy would have been free to drift — an address
    one screen accepts and the other rejects is a bug nobody sees until mail bounces.
  - `MeasurementUnits` — static; owns cm↔inch conversion for the editor, the printed
    sheet and the PDF export alike. `Convert` preserves a tailor's trailing `+`/`-`
    and returns free text unchanged; `Resolve(cm, inch, wantInches)` converts from
    whichever unit WAS filled in, which is what stopped inch printing from dropping
    95% of stored values.
  - `Order` — `OrderDate` is the day the order was TAKEN, stored UTC and editable in the form, so an
    order written down on Monday and typed up on Wednesday is filed under Monday.
    `ResolveOrderDate(picked, recorded)` is the rule: an unchanged DAY returns the recorded instant
    untouched (which is what keeps an untouched save off EF's modified list), a changed one is stored
    as that day's local midnight in UTC. `OrderDateLocal` is the read side and is what every surface
    binds — the list, the detail panel and the receipt all show the shop's own day.
    Also customer + per-section (Alteration / CustomMade / Clothing) money
    fields, **a payment method per portion** (deposit + final balance), **a tax rate
    per portion** (`XxxTaxRate` = deposit stage, `XxxFinalTaxRate` = final stage;
    a null final rate means a pre-split order whose single rate applies to both),
    cleared flags, status; many `[NotMapped]` computed totals/residuals. Money is
    derived through the static `Order.CalculateSectionPayment(...)` → `SectionPayment`
    record struct (per-**portion** tax: a portion is taxed only when its own
    method is Card, at its own rate; deposit is pre-tax and clamped to subtotal). Per-section
    `XxxMoney` accessors feed `XxxTotal`/`XxxTax`, `ReceivedDownpayment`
    (`Order.Fields.ReceivedDownpayment`), `TotalTax`, `FinalBalance`
    (`Order.Fields.FinalBalance`), `ReceivedFinalBalance`
    (`Order.Fields.ReceivedFinalBalance`), and
    the `IsSectionCleared`/`SectionResidual`/`SectionReceivedFinal` helpers.
    Per-section `XxxAddedToReceipt` gates (`total > 0 && deposit method selected`)
    are shared by the receipt and detail panel; `Items` collection. The
    `HasCustomMadeService` `[NotMapped]` gate (any custom-made record with a
    garment carrying a cm/inch value) drives the Custom Service list flag and gates the
    measurement print actions. `IsRefunded` (Status Cancelled/Returned) +
    `PaymentStatusKind` (`BalanceStatusKind` enum: Outstanding / ClearedPickedUp /
    ClearedNotPickedUp / Refunded) are the single source of truth for the
    balance-status indicator (label + colour) across the list, detail panel and
    receipt; `IsPickedUp` covers **Shipped or Completed**. Cancel/return reason is
    stored as a pair: `StatusReasonCategory` (stable key — CustomerDoesNotWant /
    ServiceUnsatisfactory / ProductIssue / PriceTooHigh / Other) plus
    `StatusReason` (free text, only meaningful for `Other`).
  - `PaymentTaxRules` / `PaymentTaxRule` (`Models/PaymentTaxRules.cs`) — a shop's tax rule per
    payment method (taxable + rate). Persisted on `Shop.PaymentTaxRulesJson`; the static `Active`
    is assigned in `App.ApplyActiveShop` and is what `Order.CalculateSectionPayment` consults to
    decide whether a portion is taxed at all. `ConfigurableMethods` drives the settings UI;
    `Normalize` maps the legacy `PaymentMethod.Card` onto `DebitCard`. Deliberately in Models
    rather than Services — the money calculation cannot resolve without it.
  - `TaxJurisdiction` (`Models/TaxJurisdiction.cs`) + `TaxJurisdictions` (`Services/`) — one shipped
    tax PRESET per store location (`Code`, `StandardRatePercent`, `PricesIncludeTax`,
    `DefaultCurrency`), loaded once from `Settings/System/Defaults/tax-jurisdictions.json` with a
    built-in home-market (`CA`) fallback so a missing or corrupt file cannot leave the app unable
    to price. Shaped after `ShopCurrencies`: a bounded shipped set the UI reads to seed a shop. The
    location is stored on `Shop.LocationCode` (null = never located → home market) and its pricing
    MODE is frozen onto `Order.PricesIncludeTax` at save, exactly as `CurrencyType` is.
    `PaymentTaxRules.CreateForStandardRate` is the seed a picked location applies.
    The tax-EXCLUSIVE entries (`CA`, `US`) quote `standardRatePercent: 0` — sales tax is added
    separately at settlement there and the rate is the shop's to enter, so picking one seeds every
    method TAX FREE and its display name omits the `{0}` ("Canada (sales tax added separately)").
    A zero is "nothing to assert", not "no tax"; the built-in fallback matches.
    REGIONS are supported but not shipped: Canada is ONE entry, not one per province. A code is
    free-form and a region is `<country>-<region>`, so re-adding `CA-ON` is a line of JSON plus a
    language key — the `TaxJurisdiction.CA-*` keys are still in all five files, marked dormant, for
    exactly that. `TaxJurisdictions.For` widens an unshipped regional code to its COUNTRY entry
    (`CA-ON` → `CA`) before falling back to the home market, which is what keeps every shop stored
    under the old provincial codes Canadian; `Find` stays strict, and the stored code is never
    rewritten, so a re-added province takes effect on its own.
    In an INCLUSIVE location `StandardRatePercent` is the only rate in play:
    `TaxJurisdictions.IncludedTaxRatePercent(shop)` is what the order editor uses for both portions,
    the per-method matrix is not consulted at all (a value-added tax cannot vary by tender), and Shop
    Settings shows the rate in place of the matrix.
    A jurisdiction also names the TAX ITSELF where prices include it (`TaxNameLabel` → a
    `TaxName.<name>` key; `Vat` for CN/FR/ES, `ConsumptionTax` for JP, absent on the exclusive
    entries because nothing reads it there). That is the word on "Includes VAT (6%)", and it is a
    DIFFERENT question from the tax number below — Japan issues a qualified-invoice number for a
    consumption tax, so deriving one from the other prints the wrong word.
    A jurisdiction also names the TAX NUMBER its businesses are issued (`TaxNumberLabel` → a
    `TaxNumber.<name>` key; `GstHst` for Canada and any Canadian region, `Vat` by FR/ES,
    `ChinaTaxpayer`, `JapanInvoice`), or omits it where none is issued — the US. `CollectsTaxNumber`
    gates whether Shop Settings asks for one at all; `TaxNumberKey` / `TaxNumberName` name it on
    screen and on the receipt line, falling back to `TaxNumber.Generic` so a stored number is never
    printed unlabelled. This is declared per jurisdiction and **not** inferred from
    `PricesIncludeTax`: Canada's GST/HST is a consumption tax quoted tax-exclusive.
  - `PhoneCountry` (`Models/`) + `PhoneCountries` (`Services/`) — one shipped phone rule per country
    (`Code`, `DialCode`, `NationalDigits`), from
    `Settings/System/Defaults/phone-countries.json`, cached with a one-entry fallback. Shaped after
    `TaxJurisdiction(s)` and deliberately a SEPARATE file from it: a tax jurisdiction is a market this
    build sells into, a phone country is anywhere a customer's number comes from, and the two lists
    stop matching the first time a shop serves a visitor. `ForShop` decides what a field opens on —
    the shop's LOCATION (widening `CA-ON` → `CA`), else the location its currency implies, else the
    home market; location first because the currency table maps EUR to France and would open every
    Spanish number on +33. `Split`/`Compose` turn a stored `"+86 138 0013 8000"` into (country,
    national) and back, longest dial code first, leaving an unrecognised legacy number WHOLE.
  - `PaymentMethod` — `Etransfer`, `Card` (LEGACY, never delete: orders saved before the split
    still hold it), `Cash`, `None`, `DebitCard`, `CreditCard`.
  - `OrderNumberMode` (`Models/Shop.cs`) — Timestamp (default, the format the app always produced)
    / Sequential / DailySequential / YearlySequential.
  - `PaymentSplitLine` / `SectionPaymentSplit` / `OrderPaymentSplits` (`Models/PaymentSplit.cs`) — one
    stage paid with several payment types (v4.0). A line is (method, amount, frozen rate); a section
    holds `Enabled` plus a line list per stage; the order holds all three sections in ONE column,
    `Orders.PaymentSplitsJson`, null for every order written before v4.0. `Enabled` is stored rather
    than inferred from "are there lines", because a shop can turn the split on before typing anything
    and a half-filled split that silently reverted would charge a different tax than the screen showed.
  - `SectionPaymentInput` — everything one section's money is computed from, passed as a STRUCT so a
    call site cannot forget the split. The parameter list had already reached the S107 limit, but the
    real reason is the pricing-mode flag: it shipped optional, a harness kept the shorter overload and
    the old arithmetic, and nothing failed to build while the numbers stopped agreeing.
  - `SectionPayment` — immutable `readonly record struct`
    (Subtotal, Deposit, FinalBase, ReceivedDownpayment, FinalCharge, Total, Tax)
    holding one section's money split, plus `DepositTax` / `FinalTax` / `PricesIncludeTax` as init
    properties and a computed `DepositStageTotal`. The per-portion tax is CARRIED rather than
    re-derived downstream as `Received − Deposit`: that difference is zero when tax is embedded in the
    price, which is how the receipt came to print "tax 0" beside a non-zero total.
    `Order.IncludedTaxRatePercent` is the companion on the ORDER: the single rate an inclusive order
    quotes, read off the first charged section because every section carries the jurisdiction's rate by
    construction in that mode. Zero on an exclusive order, where the sections legitimately differ and
    no single rate exists to name.
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
  - `ILocalizedText` — *somewhere* to read UI text from: an indexer and `Format`, nothing else.
    Implemented by both the service and a scope. A helper that composes a localized string takes
    THIS, not the service — taking the service hard-wires "the language the whole application is in"
    into code that has no opinion on the matter, which is precisely what a preview panel breaks.
  - `LocalizationService` — singleton `Instance`, indexer `["Key"]`, `Format`,
    `GetText(key, languageCode)` for a NAMED language, `LanguageChanged` event; reads the
    per-language files. The application's own setting: switching it, persisting it, listing what is
    available. Reach for it only when you mean exactly that.
  - `LocalizationScope` — one panel's own language, independent of the application's (v5.0.0). Same
    indexer, so a panel declares one in its `Resources` and changes one word at each binding site
    (`Source={StaticResource Scope}` in place of the singleton). A fresh scope FOLLOWS the
    application and re-renders with it; assigning `LanguageCode` pins it, `Follow()` lets go. It
    subscribes to the singleton, so a window that opens one must `Detach()` it on close — the same
    rule, and the same leak, as `MainViewModel.Detach`.
    A `Window`'s own properties (its `Title`) are set BEFORE its `Resources` exist, so a title
    cannot be bound to a scope declared there; set it from code.
  - `LanguagePreferenceStore` — persists the chosen language code.
- **Converters/** — `CurrencyAmountConverter`, `LocalizationLookupConverter`,
  `NullToVisibilityConverter`, `OrderStatusToLocalizedTextConverter`,
  `CustomMadeRecordSummaryConverter`, `OrderPaymentSummaryConverter`,
  `PositiveAmountToVisibilityConverter`,
  `TaxLabelConverter` (binds the whole `Order`; `Order.Fields.TaxAmount` where tax is added at
  settlement, and `Order.Fields.IncludedTaxLabel` — the tax's own name plus its rate, "Includes VAT
  (6%)" — where it is already in the price, because subtotal + tax = total holds in only one of the two
  modes. Its `static Label(Order)` is what the printed receipt calls, so paper and screen cannot
  word the same figure differently), `CustomMadeServiceFlagConverter`
  (binds the whole `Order` row; ConverterParameter `Flag` → localized
  `CustomMade.Flag.Yes`/`.No`,
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
  - `MainViewModel` — order list, paging, search, **the selection**, batch delete, batch **copy
    order**; **column sorting** (`SortBy(key)` + `SortKey`/`SortAscending` state +
    `GetSortSelector`, applied over the whole filtered set before paging in `RebuildOrdersView`);
    `DatabaseFilePath` (WPF-bound, kept instance).
    - **Selection** (v4.2.0): `SelectedOrder` is the ANCHOR (the row the detail panel describes and
      the one every single-record action works on); `SelectedOrders` is the whole selection, pushed
      in by the view through `SetSelection` because `ListView.SelectedItems` is not a dependency
      property and cannot be bound. `SelectionCount` / `HasSelection` / `HasBatchSelection` (>1) /
      `HasSingleSelection` (==1) are what the XAML gates on, and `SelectionSummary` is the count
      badge. `SelectionRequested` asks the view to select given rows — raised after a batch copy so
      the copies end up selected, which is what a single copy always did through `SelectedOrder`.
      `RebuildOrdersView` COLLAPSES the selection to the anchor: Ctrl+A means "this page", so a
      selection must not survive a search, a sort or a page turn.
    - `DeleteOrderCommand` → `ConfirmAndDeleteSelectedAsync` (owns the one MessageBox, naming a
      single order or counting several) → `DeleteSelectedAsync` (does the work, no dialog, so a
      harness can drive it — same split as `TryValidateForSave`/`ValidateForSave`). One query over
      the id set and ONE `SaveChanges`, so a failure part way leaves nothing half deleted.
    - `CopyOrderCommand` → `CopySelectedAsync` → `CopyOneOrderAsync` per record (deep-copy the
      aggregate, reset a closed status to `Processing`). Its number comes from
      `OrderNumberFormatter.Reserve` + `CommitSequence`, exactly as a new order's does; it used to be
      a hand-built `ORD-{timestamp}`. One scope and one save PER COPY on purpose — `Reserve` asks the
      database what is taken and EF cannot see added-but-unsaved rows, so a single batched save would
      give every copy the same number.
  - `RelayCommand` — `ICommand` helper.
- **Views/**
  - `MainWindow` — split into a SYSTEM bar (Local Configuration on the left; greeting, language, Store Members and
    Sign Out on the right) and a RECORDS panel that owns its own action bar (Add / Edit / Delete / Refresh plus a
    count badge bound to `MainViewModel.FilteredCount`, and beside it a `WarningSoftBrush` badge
    carrying `MainViewModel.SelectionSummary`, shown only while MORE THAN ONE record is selected —
    how far Delete reaches has to be on screen, since ctrl-picked rows can be scrolled out of
    sight). The greeting block carries a second line
    naming the languages the open shop installs (`ShopLanguages.InstalledSummary`); the language
    toggle beside it is scoped by `ShopLanguages.Selectable` and HIDDEN when that leaves one
    option. Both are rebuilt by `RefreshLanguageScope` from `ApplyRolePermissions`, so a shop
    switch re-scopes them, and both are re-rendered from `OnLanguageChangedGlobally` because they
    are written from code rather than bound. Order list + detail + paging. The list is a **`ListView` +
    `GridView`** (not a DataGrid), **`SelectionMode="Extended"`** since v4.2.0 (ctrl-click toggles a
    row, shift-click takes a run, and Ctrl+A selects the page — `ListBox` handles that gesture in its
    own `OnKeyDown`, so the mode is the whole of what the app supplies; the "page" scoping is free,
    because paging happens in the view model and the list only ever holds one page). Copy and Delete
    act on the whole selection; **every other action is gated on `HasSingleSelection`** — Edit/View,
    the three Print entries, `Enter` and the double-click — because opening or printing "the" order
    is not a question a multiple selection answers. It carries a right-click `ListView.ContextMenu`
    (Edit/Copy/Delete/Print) and a `PreviewMouseRightButtonDown` row-select
    `EventSetter` that REPLACES a selection it lands outside of and leaves one it lands inside alone
    (plain `IsSelected = true` ADDS in Extended mode, which would let the menu reach one more record
    than was pointed at), keyboard shortcuts (`Enter` = open/details, `Delete` = delete
    the selection), and **clickable column headers that sort** (asc/desc toggle + ▲/▼
    glyph) via the `GridViewColumnHeader.Click` handler and the
    `OrderColumnSort` attached properties. The Edit toolbar button + context-menu
    item relabel to "Toolbar.ViewOrder" for read-only orders
    (`RefreshToolbarLabels`). **Every column is ONE LINE:** cells derive from the theme's
    `ListCellText` (`NoWrap` + `CharacterEllipsis`), full values sit in tooltips, and both
    scrollbars are `Auto` — so no row can end up taller than another, which is the whole point of a
    list read by scanning down a column. The **Custom Service** column (via
    `CustomMadeServiceFlagConverter`: `CustomMade.Flag.Yes`/`.No` + bracketed garment names) is a `Grid` with an
    `Auto` + `*` pair rather than a stack: a horizontal StackPanel measures its children with
    infinite width, so the names would never learn they had overflowed and the ellipsis would never
    appear;
    the former Last Modified column moved into the detail panel (ordering still
    defaults to LastModifiedDate desc in `LoadOrdersAsync`). Rows gray out by
    status: **Cancelled/Returned** (`IsRefunded`) are the lightest gray,
    **Completed/Shipped** (`IsPickedUp`) a bit darker. When
    `SelectedOrder.HasCustomMadeService` is true, the Print toolbar submenu and the
    row context menu expose **Print Measurements** (measurements only) and
    **Print Receipt & All Measurements** (receipt + measurements); both open
    `MeasurementPrintOptionsWindow` then print via `PrintDialog` + `FlowDocument`
    (`PrintMeasurements`/`BuildMeasurementDocument`/`AddMeasurementSections`, the
    latter starting on a fresh page when appended after a receipt). Measurement
    language/unit come from the dialog; the receipt portion stays in the UI
    language. Detail-panel service sections are shown/hidden via
    the `Order.XxxAddedToReceipt` gates, and show the
    `Order.Fields.Downpayment`/`.ReceivedDownpayment` and
    `Order.Fields.FinalBalance`/`.ReceivedFinalBalance` pairs.
    The toolbar carries a `Local Configuration` (`Toolbar.LocalConfig`) `Menu` holding
    Add or Change Header & Footer, Currency Setup, Measurement Terms, a `Local Database` submenu (copy path / reveal
    file / open folder) and a **Import/Export** (`Toolbar.ImportExport`) submenu with
    Export+Import pairs, in order: `Toolbar.HeaderFooter` (JSON + base64 logo via
    `ReceiptBrandingStore`), `Toolbar.MeasurementTerms` (JSON via
    `MeasurementTermsService`), `Toolbar.LocalDatabase` (zip package via
    `DatabasePathProvider`), then a separator and `Toolbar.GlobalSettings`
    (everything at once via `GlobalSettingsPackage`). Every import confirms with a
    Yes/No warning dialog first and reports through `MainViewModel.StatusMessage`;
    export file names get a date suffix via `BuildDatedExportFileName`.
  - `PhoneNumberField` (`Controls/PhoneNumberField.xaml`) — the ONE phone control, used by all five
    fields that collect a number (order customer, custom-made record, the shop's own, and both staff
    screens). A dial-code picker with a drawn flag in front of the number box. Exposes `Load(stored,
    shop)` / `FullNumber` / `NationalNumber` / `SelectedCountry`, `IsValid` (the picked country's
    national length) beside `IsValidLoose` (the pre-existing 7–15 rule), `ValidationMessage`,
    `IsReadOnlyField`, `FollowLocation`, and `PhoneChanged` / `PhoneCommitted`. The country is per
    NUMBER, never per shop — a Toronto shop takes a Shanghai mobile. Storage is unchanged: one string
    column holding `"+1 905-401-6667"`, no migration, legacy numbers left as they are.
    **Strict validation applies to NEW records only** (`_existing is null`, or the create-member form);
    an existing record keeps the loose rule so it stays saveable.
  - `Themes/Flags.xaml` — six vector flags as `DrawingImage`, keyed `Flag.<code>`, merged INTO
    `AppTheme.xaml` by absolute pack URI (see `context.md` for why relative fails in a harness).
  - `OrderColumnSort` (static, in `MainWindow.xaml.cs`) — attached properties
    `SortKey` (per-column sort member) and `SortGlyph` (header arrow), consumed by
    the header `ContentTemplate` and `UpdateSortGlyphs`.
  - `OrderEditWindow` — the large create/edit form. Its Basic Info card carries an **order-date
    picker** in the right-hand input column (`InitializeOrderDatePicker` seeds it from the order's own
    day and blacks out everything after the later of today and that day; `IsOrderDateAllowed` refuses
    a future date at save; `ApplyEditableFields` writes it through `Order.ResolveOrderDate`). The
    window sets `FrameworkElement.Language` from the string table (`ApplyCalendarLanguage`, re-run on
    every language change) because a `Calendar` renders its month names from that, not from the
    string table. Then per-section pricing &
    payment, a **stage-aware tax-rate box** (one box per section that edits the
    deposit rate until the deposit is marked received and the final-balance rate
    afterwards, with a label naming the stage — `PaymentSectionControls` holds both
    rates plus `ShowingFinalRate`/`IsFinalStage`, resolved by `ApplyStageTaxRates` /
    `ResolveStageRate` and seeded by `LoadStageTaxRates`), computed summary,
    "clear all balances" master checkbox, and the
    "OrderEdit.PickedUp" quick-complete checkbox that locks the status dropdown.
    It carries **two breakdown vocabularies**. Where the price already contains the tax, the rate box
    stops naming a stage and names the TAX (`Order.Fields.IncludedTaxRateLabel`, one rate for both
    portions), the price and deposit labels become the `Inclusive*` keys, `DepositBreakdownPanel` is
    collapsed outright — every line of it would be the price restated — and `FinalInclusivePanel`
    replaces `FinalBreakdownPanel` with price / received deposit / balance due / still outstanding /
    received balance when paid, plus one line naming the embedded tax and its rate. The two final
    panels are siblings, never both visible, and BOTH are written in one pass by
    `UpdateTaxBreakdownLines` → `UpdateDueAndReceivedLines` + `UpdateInclusiveBreakdown` from one
    `SectionPayment`. The mode comes from the ORDER (frozen at save), not from the shop, so a saved
    order keeps the layout it was written in; `UpdateSectionVisibility` takes it as an argument rather
    than reading the window, which keeps it a pure function of the section and the mode.
    Switching the status to Status.Cancelled/Status.Returned puts the editor in a **refund lock**
    state (`_isRefunded`): every service/payment control (incl. OrderEdit.BalanceCleared)
    is disabled via `SetServiceControlsEnabled(false)`, all checkboxes (incl.
    OrderEdit.PickedUp) get the `NotApplicableCheckBox` style (red box + red strikethrough
    label + red line across the whole control), and Order.Fields.BalanceStatus shows
    Payment.Status.Refunded; customer fields + the custom-made records list stay usable so
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
    alt-language remap. Launched from the Local Configuration menu.
    **The first panel to carry a `LocalizationScope`** (v5.0.0): a `LanguageScopeSelector` in its
    header reads the whole panel — labels, title, and every term and garment NAME — in another
    language, without moving the application into it. The split it draws is DISPLAY versus
    INSTRUCTION: anything describing the terms follows the preview, while the confirmation dialogs,
    the warnings and the picker's own label stay in the reader's language. An inline rename
    deliberately writes into the language being PREVIEWED, which is what makes the screen usable for
    filling translation gaps.
  - `MeasurementTermLanguageWindow` — alt-language name editor popup (one name row
    per `LocalizationService.AvailableLanguages`); returns a langCode→name dict.
  - `MeasurementPrintOptionsWindow` — small pre-print dialog asking for the
    measurement **language** (radios from `ShopLanguages.Selectable()`, default =
    current, prompt and radios collapsed together when the shop runs in one language)
    and **unit** (cm default / inch); exposes
    `SelectedLanguageCode` + `IsInch` (set on Print). Feeds the Print Measurements /
    Print Receipt & All Measurements print paths (a print method, not save-to-PDF).
  - `ReceiptBrandingWindow` — the Header & Footer rich-text editor: a logo card
    (choose/remove + Left/Center/Right placement radios), a formatting ribbon
    (B/I/U, font size, align, colour swatches), and one tab per language each
    holding a header + footer `RichTextBox`. Persists via `ReceiptBrandingStore`;
    content is injected into the printed receipt and the measurements PDF by
    `BrandingRenderer`.
  - `DocumentPreviewWindow` — in-app image viewer (loads via `BitmapImage`
    `OnLoad` so the file is not locked).
  - `LanguageSelectionWindow` — first-run language picker.
  - `ShopPickerWindow` — chooses the shop to work in, at startup after sign-in and again for
    Switch Shop. Redesigned as a gradient header (title + signed-in chip) over shop **cards** — avatar
    tile, name, a currency / **installed languages** / order-count strip (`BuildDetails`, joining
    the languages as prose with `JoinList` inside a `JoinFragments` strip; the INSTALLED set, not
    the preferred language, because that is what the branch's people will be able to switch
    between), and the user's role in that shop as a badge —
    with a footer carrying Create Shop / User Management (administrators only), Cancel and Open. The list is
    filtered by `AuthenticationService.FilterAccessibleShops`, and the empty state distinguishes
    "no shops exist" from "none is assigned to you". Row/badge presentation comes from the shared
    `UserPresentation` helper; `ShopPickerRow` is a top-level `internal` type so its `{Binding}`-only
    members do not each need an S1144 suppression.
  - `UserManagementWindow` — administrator-only accounts screen, reached from the shop picker and from
    Local Configuration → User Management. Left: searchable account list — each row reads **`Tina Zhang (Manager, Staff)`**
    (`Users.AccountLabel`, whose whole shape including the brackets is translated), with the shop
    count under it, an avatar and a Locked badge on the administrator; search matches the name as well
    as the login. Right: identity card showing the name over the **login** plus a **Sign in as**
    button (vector icon drawn as `Path` geometry; hidden for your own account and for one delisted
    everywhere — it REPORTS the choice as `SignInAsUserName` and the caller performs the switch), a Person card editing
    first name / last name / **login** / phone / email (the login box is DISABLED for the
    administrator, whose login cannot change; a taken name is reported under the box as it is typed),
    password reset (blank = unchanged), and a **shop × role checkbox matrix**.
    **ONE Save, in the footer**, applying the whole pane — profile first, since it may rename and
    everything after has to act on the new login. The Person card deliberately has no Save of its
    own: it used to, labelled identically to the footer's, which saved only the password and roles
    and so discarded name edits. A rename asks for confirmation, but only after availability is
    settled — the shape that makes "manager AND staff in the same shop"
    expressible. Archived shops are still listed, or saving would silently strip an assignment to one.
    Writes on Save Changes rather than per tick, so a re-assignment cannot revoke access halfway through.
  - `StoreMembersWindow` — the OPEN shop's roster, opened from the main toolbar by a manager or an
    administrator. Header carries the head-count tiles (total / active / deactivated); the list shows
    each member's role and shift with an Active/Deactivated badge (delisted members stay, dimmed); the
    editor covers person (first name, last name, birthday), role in THIS shop (manager and/or staff), activation, start
    date, a read-only delisting stamp and a 15-minute shift picker. Add Member creates the account and its
    membership together. Delete Account is administrator-only — deletion reaches every shop, whereas a
    manager's tool for "they left" is deactivation, which records when. Needs no database: members come
    from `AuthenticationService` and the shop is passed in.
  - `UserPresentation` (static, `Views/UserManagement/`) — localized role name (including "no role") and the stable
    name-hashed avatar brush/initial, shared by the picker, the user manager, the roster and the main
    toolbar so the role-name switch is not copied a fourth time.
  - **Validation reporting in `OrderEditWindow`** — a refused save marks three surfaces from one path:
    a `ValidationBanner` above the form and OUTSIDE the `ScrollViewer` (what is wrong, all of it), a
    `*ErrorText` block under each input (where), and one dialog (that something is wrong now, which the
    Save button at the foot of a taller-than-the-window form cannot convey). `Fail(key, inline, focus)`
    and `TryRequireFilled(RequiredTextFields())` are the only reporters; `_validationProblems` is what
    the banner and the dialog both read. `TryValidateForSave` owns the dialog and delegates the marking
    to `ValidateForSave`, which is what a harness drives — a `MessageBox` inside a check blocks the
    thread. `ErrorText` at the foot of the window keeps a separate job: a save that THREW.
  - `StoreManagementWindow` — administrator-only shop administration, reached from the Select Shop
    footer. `SelectionMode="Extended"` (ctrl/shift click), and every action reads the whole selection.
    Reversible actions (take out of service / put back) sit in a separate card from the destructive ones
    (delete selected / reinitialize), and only the second group goes through `ConfirmDestructiveWindow` —
    keeping them apart is what stops an administrator reaching for delete because it is the button they
    recognise. Performs nothing itself: `ShopAdministration` owns the rules, `ShopArchive` owns the file
    format. `ShopsChanged` tells the picker whether to reload, so cancelling out costs no refresh.
  - `ConfirmDestructiveWindow` — the gate in front of every irreversible action: a 10-character phrase
    generated per dialog from `RandomNumberGenerator` over an alphabet with **no lookalike pairs**
    (neither half of O/0, I/1/L, S/5, Z/2, B/8, G/6, Q/O), typed case-sensitively before either button
    enables. Returns `ConfirmedAction.SaveThenProceed` or `.ProceedNow` and performs nothing — the caller
    owns what "proceed" means and is the only thing that can describe the impact. Its phrase box carries
    its OWN template rather than deriving from `ThemedTextBox`; see `context.md`, "a theme trigger with
    TargetName beats your local value".
  - `SessionActionWindow` — the Lock / Sign out chooser, raised by ESC on the main window and by the
    toolbar's Lock button (`MainWindow.OfferSessionChoiceAsync`, one entry point for both so they
    cannot drift). Reports through `Action` (`Stay` / `Lock` / `SignOut`) and NOT through
    `DialogResult`, which throws on a non-modal window and would make it undrivable by a harness.
    Closing it leaves `Stay`, so a stray ESC ends nothing. Performs nothing itself.
  - `LockScreenWindow` — how a locked session comes back: the account is fixed and named, only the
    password is asked for. Authenticates through the same `AuthenticationService.Authenticate` the
    login window uses, and accepts ONLY the account that locked the session — a different person's
    correct password is still refused, because unlocking resumes somebody else's shop, role and name
    on every order saved next. No Cancel: `OnClosing` turns every other exit into a sign-out.
    Reports through `Unlocked` / `SignOutRequested`.
  - `App.LockAsync` — closes the window, calls the real `SignOut()` (a lock that kept `CurrentUser`
    would leave every capability gate answering yes), shows the lock screen, and on success reopens
    the SAME shop through `ReopenLockedShopAsync`. The only things remembered are the account and the
    shop's `PublicId`, both in locals for the length of the method — nothing about a locked session
    survives the process. Reopening goes through `LoadSelectableShopsAsync`, so access revoked while
    the machine sat locked lands the user at sign-in instead.
  - `ShopLocalizationWindow` — the languages a shop runs in and the currencies it takes, in one
    panel because they are one decision: a language brings the currencies of its market. Languages
    left, a card per ticked language on the right listing what it brings. Opened from a link card in
    `ShopSetupWindow`; edits nothing itself, returning `InstalledLanguages` / `PreferredLanguage` /
    `SupportedCurrencies` / `PreferredCurrency` so cancelling costs nothing. **A currency reachable
    from two languages (EUR, under Français and Español) is ONE shared row object**, so the two cards
    are two views of one fact and cannot disagree. Both pickers list only what is ticked, so "opens in
    a language it runs in" and "prices in money it takes" are enforced by what the controls CONTAIN.
    `OfferedByTickedLanguages()` is what the right pane shows, and `TickedCurrencies()` is scoped to it,
    so **the panel returns exactly what it shows** — the rows are seeded from every language on the
    SYSTEM plus whatever the shop already accepted, which is a wider set than the cards display, and a
    ticked row outside it used to reach the picker and the saved record with no tick box to remove it.
    `EnsureOneCurrency()` is the floor: clearing the last currency shows the red inline line and
    re-ticks the first offered one, on every toggle and in the constructor, so an already-invalid shop
    is repaired as the panel opens rather than refused when it closes.
  - `ShopSetupWindow` — creates a shop and edits one (Local Configuration → Shop Settings). A
    scrolling card layout: shop identity (per-language names, **per-language address**, **phone /
    email / website**, and a **link card into `ShopLocalizationWindow`** carrying a one-line summary
    of the chosen languages and currencies — they used to be two tick lists and two pickers inline,
    in an already long form), the **payment /
    tax matrix** (one row per `PaymentTaxRules.ConfigurableMethods` entry — tax free vs. charge at
    its own rate, generated from a `PaymentTaxRow` view-model so a method added later needs no
    XAML change), the **receipt-number format** (prefix / padding / next number / mode with a live
    preview built through `OrderNumberFormatter`, so the preview cannot drift from what is
    actually issued), and measurement-terms seeding (creation only). **Creating** a shop is
    administrator-only; **editing** the open one is `CanConfigureShop`, so its manager may too.
    The name and address editors share one `LocalizedFieldRow` DataTemplate bound to
    `LocalizedTextEntry`, so the two per-language blocks cannot drift apart. The address is shown
    under the shop name in the `MainWindow` header (`ShopContext.CurrentAddress` / `HasAddress`), so
    the open branch is identifiable at a glance. Phone / email / website are stored but NOT yet
    printed: the receipt header/footer is already free rich text per language, so injecting them
    would double-print for any shop that typed its details there by hand.
- **Product catalogue** — `Models/ProductCatalog` + `Services/ProductCatalogService`, the ready-made
  categories an order's clothing rows offer. Per shop, one JSON file keyed on `PublicId`, seeded from
  `ProductCatalogDefaults` and edited in `Views/ProductCatalogWindow` (Local Configuration → Product Categories).
  Modelled on `MeasurementTermsService`, down to the copy-between-shops path.
  - The five shipped ids are a COMPATIBILITY SURFACE — see context.md before touching them.
    Predefined names come from the string table (`ClothingItem.<id>`); user-added ones carry their
    own per-language names. `ResolveName` always resolves, including for a deleted category, so a
    historical order never prints a blank.
- **Migrations/** — `InitialCreate`, `AddOrderPaymentFields`, and the model
  snapshot. Columns added after those two migrations arrive through the runtime
  guards in `App.xaml.cs` instead (see Startup above).
- **Controls/**
  - `LanguageScopeSelector` — the picker that drives a `LocalizationScope`, and the whole of what a
    panel needs to become previewable: declare a scope in `Resources`, drop this on the panel with
    `Scope="{StaticResource Scope}"`, bind through the scope. Fills itself from
    `ShopLanguages.Selectable()` (a preview must not offer a language the branch does not run in) and
    collapses when that leaves one option. Follows its scope in BOTH directions, so a host that moves
    the scope itself does not leave the box naming a language that is no longer on screen.
    **It renders ITSELF in the application's language, never in the previewed one** — a control that
    followed its own preview would turn Japanese the moment Japanese was picked, leaving nothing on
    screen the reader could use to get back.
  - `CalendarSizing` — attached `MatchOwnerWidth`, which makes a `DatePicker`'s drop-down calendar
    **at least** as wide as its box (a `MinWidth` floor, not a fixed `Width`: the month grid is
    content-sized and a hard width narrower than it needs clips columns off). A behavior rather than
    a binding because the Calendar lives in a `Popup`, a separate visual tree that `RelativeSource`
    cannot cross — and fails silently when it tries. The home for any future "the theme cannot
    express this" hook; see context.md.
  - `WindowFitting` — `Fit(Window)` resolves the monitor; `Fit(Window, Rect)` takes the work area as
    an argument, which is what makes the rule testable on a machine that is not small. Fits EVERY
    window to the screen it opens on, scaling the whole layout down
    proportionally (`LayoutTransform`, so the window MEASURES smaller and its minimum can come down)
    when the screen is smaller than the window was drawn for. Registered once from
    `App.StartApplicationAsync` as a `Window.Loaded` **class handler**, so a window added later is
    covered without being told to opt in — the defect it fixes was one window's `MinHeight="900"`
    against a 752-tall work area, which put the pinned Save footer permanently off screen. Reads the
    work area of the monitor the window is actually on (`MonitorFromWindow`), converted from device
    pixels to DIPs. Never scales up; floors at 0.5.
- **Animations/**
  - `PanelTransition` — the global open/close transition for panels: attached `Mode`
    (None / Fade / FadeSlide), 0.5s, cubic ease-in-out, 10px slide, with the duration and curve
    defined once. Binding-safe (it animates `Visibility` with a key-frame track rather than
    assigning it) and re-entrancy-guarded; see context.md before changing either.
- **Themes/**
  - `AppTheme.xaml` — the application's single visual language, merged in `App.xaml` so every window
    inherits it: **typography** (three families by job — UI / tabular-numeric / icon — a six-step
    size scale, and semantic text styles), the palette as named brushes (`PrimaryBrush`,
    `AccentBrush`, `HeaderGradientBrush`,
    the neutral ramp, danger/success/warning), implicit styles for Button / TextBox / PasswordBox /
    ComboBoxItem / DatePicker / CheckBox / RadioButton — the date picker and its Calendar are styled
    ONCE here and reached implicitly, so the order form's and Store Members' four are one control;
    `FontSizeCalendar` plus the day button's `MinWidth` open the drop-down out SIDEWAYS rather than
    down, and its `IsBlackedOut` trigger draws the strike the stock template would have — the ToolBar
    button key, and the keyed
    `CardBorder` / `CardHeading` / `FieldLabel` / `SectionHeading` / `RosterCardContainer` /
    `TimePickerComboBox`, plus themed `Menu` / `MenuItem` / `ContextMenu` / `Separator` (one
    MenuItem template covering all four roles, switched by `Role` triggers; `ThemedContextMenu`
    gives the right-click menu the same surface, radius and shadow as the menu-bar popups;
    `DangerMenuItem` for destructive entries; and a keyed
    `{x:Static MenuItem.SeparatorStyleKey}` style, which is the ONLY way to reach a separator
    inside a menu). Colours that encode
    MEANING (balance status, the refund strike) stay at their use sites deliberately. CheckBox and
    RadioButton are recoloured but NOT re-templated — the order editor drives dozens of them from
    code and swaps templates on some. The ComboBox template handles `IsEditable` and resolves
    `DisplayMemberPath` through `ItemTemplateSelector`; see context.md before touching it.
- **Settings/** (project root) — configuration that SHIPS with the build. Read-only at runtime,
  versioned in git, replaced wholesale by an upgrade. Deployed by a `Settings\**\*` glob in the
  csproj, so adding a file needs no project edit. Contrast `%LOCALAPPDATA%\CameywareOrder`, which
  holds everything the application WRITES and must survive an upgrade.
  - `System/Languages/<code>.lang.xml` — one document per language, root `<Language code name>`.
    Ships **zh-CN, en-US, fr-FR, es-ES, ja-JP**. **Discovered**, not registered: adding a language is
    dropping a file in. The file name is a convention; the `code` attribute inside is the identity,
    and a duplicate code is refused (naming both files, since which loads "second" is just
    alphabetical). 529 keys each, and every language must carry the same set —
    `LocalizationService.KeyGaps` computes the difference and the harness fails on it.
    - The harness additionally enforces that no key is DEAD (absent from source and not covered by a
      runtime-composed prefix), that no translation is word-identical to English outside a small
      shared allow-list, and that placeholder sets match — a stray `{1}` is a runtime
      `FormatException`, not a cosmetic slip. See context.md before deleting any key.
    - `Format.*` keys are RULES, not labels: how a language punctuates (`Format.ListSeparator`,
      `Format.BulletSeparator`). Reach them through `LocalizationService.JoinList` /
      `JoinFragments`, never by reading the separator out and joining by hand. Spaces are `&#32;`
      because a trailing space is significant — and for the same reason these files must never be
      rewritten with `XDocument.Save`. See context.md before adding to this namespace: some things
      (export filename suffix, currency symbols) deliberately do NOT belong here.
  - `System/Defaults/app-defaults.json` — `defaultLanguage`, the fact ABOUT the language set that no
    single language file can own. Read through `Configuration/AppDefaults`, which degrades to a
    fallback on every failure because startup reads it before any window exists.
- **Configuration/** — the code that locates and reads configuration.
  - `SystemSettingsPaths` — locates `Settings/System`, probing the app directory then the working
    directory. `AppDefaults` — reads `app-defaults.json` (`defaultLanguage`, `backupRetentionCount`),
    degrading to fallbacks on every failure because startup reads it before any window exists.
  - `UserDataPaths` — **the one definition** of `%LOCALAPPDATA%\CameywareOrder` and everything under
    it. Never re-derive that path: it was duplicated across six services before this existed, and
    the product has already been renamed once.
    - `Config/` — credentials, currency, language preference. Migrated LAZILY per file by
      `ResolveConfigFile`, which returns the OLD path if the move fails, so a failed tidy-up can
      never make credentials unreadable.
    - `Backups/` — safety copies taken before an import. `SweepLegacyBackups` collects strays left
      at the root by earlier versions and NEVER deletes; `PruneBackups` deletes, and only after a
      new backup supersedes an old one.
    - `orders.db`, `Documents/`, `measurement-terms-<publicId>.json` stay at the ROOT on purpose —
      the first two are named inside every export package (relative to the root), the third is
      keyed by file name. See the remarks in the class before moving any of them.
    - Every operation has an overload taking the data root, so the migration is testable against a
      throwaway folder rather than only against the machine it must not break.

## Key cross-cutting patterns

- All UI text flows through `Languages.xml` / `LocalizationService`.
- **Authorization is per shop and re-evaluated on every shop switch.** Decisions go through the
  named capability properties on `AuthenticationService`, never `role == Manager` comparisons in the
  UI. Chrome is HIDDEN rather than disabled, and every hidden action still re-checks its capability
  in the handler — a hidden menu is a fact about the UI, not a permission. `App.ApplyActiveShop`
  binds the shop before publishing it, and `MainWindow` re-gates from `ShopContext.ShopChanged`,
  because the same person can be a manager in one branch and staff in the next.
- **Which languages a person may use is a SHOP setting crossed with a capability, not a role rule.**
  A shop installs one or more of the shipped languages (`Shop.InstalledLanguagesJson`); its managers
  and staff switch between exactly those, and see no toggle at all when there is one. An
  administrator keeps every shipped language, because they work across branches. Every language
  picker in the app — the toolbar toggle, the shop editor, the measurement print dialog, the PDF
  download panel — resolves through `ShopLanguages`, never through
  `LocalizationService.AvailableLanguages` directly. The set is also STATED, not merely obeyed:
  under the main window's greeting and on every shop-picker card, so nobody has to open a toggle
  to find out what their branch supports. The login and shop-picker screens are the
  exception and stay unrestricted: no shop is open yet, and a user has to be able to read the screen
  they sign in on.
- **Membership is the unit of access, and it can be switched off.** A person's standing at a shop —
  role(s), activation, start date, shift — is one `ShopMembership`; deactivating it removes that shop
  from their view without touching any other. Sign-in is refused only when EVERY membership is
  inactive, so a suspension at one branch never costs someone their job at another. Anything that
  writes memberships must preserve the fields it does not own: the administrator's role matrix sends
  roles only, precisely so it cannot reset a roster's activation or shift.
- Per-section money math is centralized in `Order.CalculateSectionPayment` and
  reused by the model and the live editor summary so persisted and on-screen
  values match; tax is applied **per payment portion** (deposit vs. final) based
  on that portion's method **and its own rate**. Whether a portion is taxed at all
  comes from the SHOP (`PaymentTaxRules.Active`), while the rate comes from the
  ORDER — so a rule change never silently re-prices a saved order, and the order
  editor resolves rates live from the rules except on a read-only order, which
  keeps what it was actually charged. The editor persists whatever it
  displayed — both stage rates, and the final method resolved through
  `EffectiveFinalMethod` — so a reloaded order never recomputes to different
  amounts than the ones the shop saw when saving.
- The paged order list is sorted in `MainViewModel` over the whole filtered set
  before `Skip/Take`, driven by per-column `OrderColumnSort.SortKey` attached
  properties (never `Items.SortDescriptions`, which would sort one page only).
- Control-sync handlers use reentrancy guard flags (`_syncingPayment`,
  `_syncingStatus`) to avoid event loops.
- Order "picked up" state is represented purely by `OrderStatus.Completed`
  (no separate column); the "OrderEdit.PickedUp" checkbox is only enabled once the order has a
  charge and every final balance is cleared, and read-only statuses relabel the
  open action to "Toolbar.ViewOrder". **Read-only statuses are
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
  `Payment.Status.Refunded`, and on both the receipt and the detail panel the
  **`Order.Fields.PaymentBreakdown` is replaced by `Order.Fields.CancelReason`/`.ReturnReason`**
  (`ReturnReasonSummaryConverter`) — the charge lines, totals and `Order.Fields.FinalBalance` still
  print, so a refunded receipt keeps full parity with a normal one.
- Destructive actions (delete) own their confirm dialog inside the command, so
  toolbar, context menu, and the `Delete` key share one prompt.
