# TODO / Checkpoints — LeeYongeOrdering

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

### 2026-07-26 20:30 — Multi-shop + login (scalability)  [IN PROGRESS — Phase 0 of 6 DONE]
- Ask: "Lets scalable the Whole application" — add a login screen (roles Admin/Manager/Staff later, admin/admin for now, no complexity rules); after login the admin picks an existing shop or creates one; the current data becomes "Shanghai LeeYonge Bespoke"; a new shop collects name, preferred language, currency and measurement-terms setup; bilingual for now, multi-language later; role-gated behaviour left blank.
- Approved plan: `C:\Users\123\.claude\plans\crispy-wandering-moler.md`. Decisions taken with the user: ONE SHARED DATABASE with a `ShopId` column (not per-shop folders); orders always filtered to the open shop, no cross-shop view yet; switch shops via a 本地配置 menu item without re-login; global language for login, per-shop language once open; wizard seeds defaults or copies another shop; the standalone language picker is dropped so startup is login → shop picker; `credentials.json` holds a LIST of accounts from day one.
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
6. Shop UI: picker, new-shop wizard (creates the row BEFORE "Configure now" so the terms editor targets the new shop), 切换店铺 menu item + in-place reload.
7. Localization keys, orphan sweep for the removed `LanguageSelection.*`, docs.

## Completed

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

### 2026-07-26 16:30 — 全部服务总金额: confirmed-receipts only, item-driven clear-all, aligned breakdown  [DONE]
- Ask: "1. 实收定金和实收尾款一定要等相应的checkbox点击完毕之后才能执行计算。2. 当结清所有尾款的时候，对每个服务都要执行定金已收和尾款结清（没有 order item 不参与运算；有 item 但没选支付方式则默认现金；有 item 但价格为 0 仍参与运算，标注价格有误并提示用户）。3. 以上 order item 规则也要体现在 breakdown。4. breakdown 左对齐，label 与 全部服务总金额 同列，价钱在价钱列。"
- Done:
  - [x] **(1)** `Models/Order.cs` `ReceivedDownpayment` now sums through `SectionReceivedDeposit(money, XxxDownpaymentCompleted)` — a deposit only counts once its checkbox is ticked. `OrderEditWindow.RefreshPaymentSummary` mirrors it via its own `SectionReceivedDeposit(money, controls)`. 实收尾款 was already gated on `BalanceClearedCheck`, left as-is. Changed the MODEL too, not just the editor, so the saved order reports the same figure (SKILL §4).
  - [x] **(2)** `PaymentSectionControls` gained `HasItems` / `SectionTotal` / `ServiceNameKey` (Funcs + key set in `InitializePaymentSectionControls`) and a derived `HasMissingPrice`. "Has items" = custom-made records exist / clothing rows exist / **for Alterations, a non-empty price box** (it has no item list of its own — assumption stated to the user). `ApplyClearAllToSection` rewritten to take the control group: skips item-less sections, defaults a null deposit method to Cash, ticks **`DownCompletedCheck` as well as** `BalanceClearedCheck`, and treats an explicit "None" deposit as "nothing to confirm" (final method falls back to Cash). `WarnAboutUnpricedServices` shows one non-blocking `MessageBox` listing services with items but no charge.
  - [x] **(3)** `AddServiceTotalDetail` now gates on `HasItems()` instead of `total > 0`, so a zero-priced service is listed rather than dropped — flagged with `Order.Fields.ServiceTotalUnpriced` (（价格有误）) and drawn in amber (`UnpricedLineBrush`).
  - [x] **(4)** Summary `Grid` got `Grid.IsSharedSizeScope="True"` and its label column `SharedSizeGroup="SummaryLabel"`; the breakdown panel now spans columns 0–1 and each line is built by `BuildBreakdownRow` as its own 2-column `Grid` joining that shared-size group. Labels line up under 全部服务总金额, amounts under its figure.
  - [x] Knock-on fix: `IsOrderBalanceCleared` gated on `_totalAmount <= 0m`, so an order made only of zero-priced items could never be cleared — `RefreshPaymentSummary`'s `ClearAllBalancesCheck.IsChecked = cleared` would have sprung the tick straight back off, defeating rule 2's third bullet. Now gated on "no section has items".
  - [x] `Languages.xml` (zh-CN + en-US): `Order.Fields.ServiceTotalUnpriced`, `OrderEdit.Warn.UnpricedServices`; `Order.Fields.ServiceTotalLine`/`LineNoDetail` reshaped to label-only ({2} amount placeholder dropped) now that the amount is its own column.
- Notes: build succeeded 0 warnings / 0 errors. Deliberate decisions reported to the user: (a) unticking 结清所有尾款 clears the balance ticks but leaves 已收定金 set — a received deposit is a fact, and `ClearAllBalancesCheck` is also driven by derived state, so reverting it would silently wipe manual ticks; (b) **known limitation** — `Order.IsBalanceCleared` still gates on `TotalAmount <= 0m` and `ApplyPaymentFields` persists a zero-total section as absent, so an order whose services are ALL zero-priced still reads Outstanding once saved. Fixing that reaches into `XxxAddedToReceipt` and the printed receipt, well beyond this ask.

### 2026-07-26 16:00 — Small-print service breakdown under 全部服务总金额  [DONE]
- Ask: "在订单中，全部服务总金额下方用小字给出breakdown，比如 修改衣服（服装修改）：$123 / 定制服务 (衬衫、西装)：$1234"
- Done:
  - [x] `Views/OrderEditWindow.xaml`: summary card gained a row — `ServicesTotalBreakdownPanel` sits in column 1 directly under `TotalAmountText`; the 实收定金 / 剩余尾款 / 余额状态 rows and `FinalBalanceBreakdownPanel` shifted down one (rows 2/3/4).
  - [x] `Views/OrderEditWindow.xaml.cs`: `RefreshServicesTotalBreakdown()` (called from `RefreshAllServicesTotalAmount`, right after the total) emits one small grey line per section whose total > 0, via `AddServiceTotalDetail`. Parenthetical detail per section: Alterations → its localized service category (`AlterationDetailText`), CustomMade → the measured garment names (`CustomMadeDetailText`), ReadyMade → the distinct item categories actually priced (`ClothingDetailText`). Shared `ListSeparator` (zh 、 / en ", ") matches `CustomMadeServiceFlagConverter`; `LocalizeWithFallback` keeps legacy free-text category values readable.
  - [x] `Services/CustomMadeMeasurementReader`: new `GetGarmentNames(IEnumerable<CustomMadeServiceRecord>, languageCode)` overload; the existing `Order` overload delegates to it. The editor holds unsaved records rather than an `Order`, and this avoids a second copy of the garment-name extraction.
  - [x] **Refresh gap closed**: neither `AlterationCategoryBox` nor the clothing rows' `categoryBox` had any change handler, so both parentheticals would have gone stale on edit. Added `OnServiceCategoryChanged` (XAML `SelectionChanged`) and a `categoryBox.SelectionChanged` subscription in `AddClothingItemRow`; both call `RefreshServicesTotalBreakdown()` only — a category carries no money, so a full `RefreshComputedTotals` would be wrong-altitude.
  - [x] `Languages.xml` (zh-CN + en-US): `Order.Fields.ServiceTotalLine` (`·  {0}（{1}）：{2}` / `·  {0} ({1}): {2}`) and `Order.Fields.ServiceTotalLineNoDetail`. The **whole line shape** lives in the string table because the parentheses and colon are fullwidth in Chinese and ASCII in English — concatenating them in C# would have produced `Alterations（Garment Adjustments）：$123` in English.
- Notes: build succeeded 0 warnings / 0 errors. Presentation only — no model, schema or calculation change; the lines are the same three section totals the grand total is summed from. GOTCHA recorded: a new brush had to be `System.Windows.Media`-qualified (ImplicitUsings makes bare `Color`/`SolidColorBrush` ambiguous against QuestPDF/HotChocolate) and is created once via `CreateFrozenBrush` rather than per line.

### 2026-07-26 15:40 — Final breakdown: per-portion tax split under 此服务总计税  [DONE]
- Ask: "Ok, in the final breakdown on the calculation in 尾款支付方式 > breakdown 此服务总计税 having little fonts under it. -定金（现金）税收： $0 / -尾款（银行卡）税收：$XXX"
- Done:
  - [x] `Views/OrderEditWindow.xaml`: new `TaxBreakdownLine` style (FontSize 11, #7A8698, wrapping). All 3 `*FinalBreakdownPanel` grids gained a 6th row: a `StackPanel` in column 1 directly under the 此服务总计税 amount holding `*DepositTaxLineText` + `*FinalTaxLineText`; 税后总价 and 剩余尾款 shifted to rows 4/5.
  - [x] `Views/OrderEditWindow.xaml.cs`: `PaymentSectionControls` gained `DepositTaxLine`/`FinalTaxLine`; new `UpdateTaxBreakdownLines(c, money)` (called from all 3 `Refresh*Totals` right after the total-tax line) fills them from `money.ReceivedDownpayment - money.Deposit` and `money.FinalCharge - money.FinalBase`; `PaymentMethodName` resolves the method label (null → `PaymentMethod.None`). The final line uses `EffectiveFinalMethod`, so the named method always matches the one the tax was actually computed with.
  - [x] `Languages.xml` (zh-CN + en-US): `Order.Fields.DepositTaxLine` (定金（{0}）税收：{1} / "Deposit ({0}) tax: {1}") and `Order.Fields.FinalTaxLine` (尾款（{0}）税收：{1} / "Final balance ({0}) tax: {1}"). Both formatted via `LocalizationService.Format`, so the separator/punctuation stays per-language.
- Notes: build succeeded 0 warnings / 0 errors. Presentation only — no model, schema or calculation change; the two lines always sum to the 此服务总计税 figure above them. Applied to all 3 sections, not just Alterations, since they share the panel shape.

### 2026-07-26 15:15 — Per-stage tax rate: one shared 税率 box that follows the deposit / final stage  [DONE]
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
  - [x] 当前计税 now shows the section's whole tax (`money.Tax`) in all 3 sections instead of only the deposit's tax (`ReceivedDownpayment - Deposit`), so it pairs with the 税后总价 line beneath it. `Languages.xml` en value `Order.Fields.DepositTax` "Tax on Deposit" → "Current Tax" (zh 当前计税 already correct; value change, not a new key).
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
  - [x] **MainWindow**: documented the 本地配置 menu incl. the 导入/导出 submenu (3 export/import pairs, confirm dialogs, dated file names).
  - [x] **Cross-cutting**: corrected the refunded-receipt rule (it no longer omits 剩余尾款 — only the 收款明细 breakdown is swapped for the reason); recorded that read-only status = Completed/Shipped/Cancelled/Returned across the three predicates that must change together; added the self-contained Import/Export rule.
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
- Root cause: the 6 payment "已收定金"/"当前服务尾款已结清" checkboxes (Alterations/CustomMade/Clothing × deposit-completed + balance-cleared) sit directly inside **vertical** `StackPanel`s, where children default to `HorizontalAlignment="Stretch"` (fill the row width) — unlike the working `已取货`/`结清所有尾款` checkboxes, which sit in a **horizontal** `StackPanel` (children auto-sized, not stretched). The shared `NotApplicableCheckBox` style's strikethrough `Line` (`Stretch="Fill"`) then filled the checkbox's full stretched render width instead of just its content.
- Done:
  - [x] `Views/OrderEditWindow.xaml`: added `HorizontalAlignment="Left"` to `AlterationDownCompletedCheck`, `AlterationBalanceClearedCheck`, `CustomMadeDownCompletedCheck`, `CustomMadeBalanceClearedCheck`, `ClothingDownCompletedCheck`, `ClothingBalanceClearedCheck` — content-sized ("inline-block") like the other checkboxes, so the red strikethrough now only spans the checkbox + label.
- Notes: build succeeded 0 warnings/errors. XAML-only fix; no code-behind/Sonar-relevant changes (style itself unchanged).

### 2026-07-26 12:30 — Bug fix: Shipped orders are now read-only (view-only)  [DONE]
- Ask: "bug fix: If the order is shipped, the order record is considered as completed, shouldnot be modifed anymore but can view."
- Done:
  - [x] `MainWindow.xaml.cs` `IsReadOnlyStatus` (drives the toolbar/context-menu 编辑/查看 label): added `OrderStatus.Shipped` alongside Completed/Cancelled/Returned.
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
  - [x] `MainWindow.xaml` (detail panel bug fix): wrapped the 收款明细/PaymentBreakdown label+value in a `StackPanel` that collapses via `DataTrigger` when `SelectedOrder.IsRefunded`; added a new label+value block (MultiBinding → `ReturnReasonSummaryConverter`) shown only when `IsRefunded`, in the same slot.
  - [x] `MainWindow.xaml.cs` (`AddReceiptTotals`): full charge/payment breakdown (item sections, totals, downpayment, final balance) now always renders regardless of refund status (reverted the earlier "hide everything" approach); only the payment-method-breakdown paragraph is swapped for a 取消原因/退货原因 section (via `ReturnReasonSummaryConverter.Resolve`) when `order.IsRefunded`.
  - [x] `Languages.xml` (zh-CN + en-US): `ReturnReason.CustomerDoesNotWant/ServiceUnsatisfactory/ProductIssue/PriceTooHigh/Other`, `OrderEdit.Validate.StatusReasonRequired`, `OrderEdit.Validate.StatusReasonOtherRequired`.
- Notes: build succeeded 0 warnings/errors; SonarQube clean on all changed files. No further DB migration beyond the new column. Receipt printing and the PDF share the exact same `BuildReceiptDocument`/`AddReceiptTotals` code path, so the fix covers both automatically.

### 2026-07-26 11:30 — Receipt: hide charge breakdown for cancelled/returned orders, show reason instead  [DONE]
- Ask: "小票页面内容优化 1. 如果已退货或者取消，那么不需要展示收费明细，但是需要标出退货或者取消原因。->这应该也要在打印PDF中体现出来"
- Done:
  - [x] `MainWindow.xaml.cs` `BuildReceiptDocument`: when `order.IsRefunded`, skips `AddAlterationReceiptSection`/`AddClothingReceiptSection`/`AddCustomMadeReceiptSection`/`AddReceiptTotals` (the whole charge/payment breakdown) and calls new `AddRefundedReceiptSummary` instead — shows only the coloured 余额状态 line + a 取消原因/退货原因 section (label picked by `order.Status`) with `order.StatusReason` (falls back to "-" when blank), then Notes if present. Removed the now-dead `!order.IsRefunded` guard inside `AddReceiptTotals` (that method is only ever called for non-refunded orders now).
  - [x] `Languages.xml` (zh-CN + en-US): `Order.Fields.CancelReason` (取消原因/Cancellation Reason), `Order.Fields.ReturnReason` (退货原因/Return Reason).
- Notes: build succeeded 0 warnings/errors; SonarQube clean on `MainWindow.xaml.cs`. Receipt printing (`OnPrintReceiptClick`) and the "print receipt + measurements" flow both reuse `BuildReceiptDocument`, and printing to PDF goes through the same `PrintDialog`/FlowDocument path (via a PDF printer driver) — so this single change covers both the on-screen print and the PDF output, per the ask. No DB/schema change (reuses `StatusReason` added in the previous session).

### 2026-07-26 11:00 — Cancel/return reason box, address-required-on-Shipped validation, basic-info beautify  [DONE]
- Ask: "TODO: 如果订单状态为已取消或者已退货, 1. 在订单界面地址栏下方生成一个textbox写退货理由 如果取消那就变成取消理由，里面有placeholder让用户输入退货/取消理由 2. 如果状态改为已发货，更改/保存时地址一栏不能为空，要有validation 3. 优化编辑菜单的整体页面，现在inputbox, radio button还有textbox太单调。美化页面同时增加一些icon让页面更加美感。"
- Done:
  - [x] `Models/Order.cs`: new nullable `StatusReason` string property (backs both 取消理由/退货理由, same field).
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
- Findings: 3 Import/Export features exist (量身项目设置 JSON, 本地数据库, 页眉页脚 JSON). Measurement terms has no media. Branding logo was already self-contained (base64 in JSON, from the prior session). **Gap found:** custom-made document images (`CustomMadeDocument`/`DocumentStorageService`) live as files under `LocalAppData/LeeYongeOrdering/Documents/CustomMade`, referenced only by `StoredFileName` inside `Order.CustomMadeRecordsJson` — the DB export/import only copied `orders.db` (+wal/shm), so migrating the DB to another PC left every attached image reference dangling.
- Done:
  - [x] `Data/DatabasePathProvider.cs`: `ExportDatabaseTo` now writes a zip package (`orders.db` + wal/shm sidecars + the entire `Documents/` folder tree) instead of a raw `.db` copy. `ImportDatabaseFrom` tries the zip package first (validates it contains an `orders.db` entry, extracts with zip-slip path-containment guarding via `ExtractPackageSafely`), and falls back to the legacy raw-`.db`-copy path (catches `InvalidDataException` from `ZipFile.OpenRead`) for backward compatibility with previously-exported plain `.db` files. Both the current db AND the current `Documents/` folder are backed up (`orders.db.bak-<ts>`, `Documents.bak-<ts>`) before being overwritten.
  - [x] `MainWindow.xaml.cs`: export/import dialogs now default to `.zip` (`orders-backup.zip`, filter "Backup Package (*.zip)"), import dialog also still accepts legacy `.db`/`*.*`.
  - [x] `Languages.xml` (zh-CN + en-US): reworded `ImportExport.DatabaseConfirm` to mention attached images are included/backed up too.
- Notes: build succeeded 0 warnings / 0 errors; SonarQube clean on `DatabasePathProvider.cs` and `MainWindow.xaml.cs`. No DB schema change (images stay file-based, not blobbed into SQLite — bundling via zip achieves the same "self-contained migration" goal without an invasive schema/perf tradeoff). See `/memories/repo/startup.md` for the durable rule going forward: any future Import/Export of a record/config with attached media must keep the export self-contained (base64-in-JSON for small single assets like the logo, or a bundled zip package for larger/many files like custom-made document images).

### 2026-07-26 09:00 — Import/Export for 页眉页脚 (header/footer branding)  [DONE]
- Ask: "agent skill: wpf-dev, Previous features: Add a new tab on navigation called Import/Export under the local configuration... TODO: Add Import/export for 添加或更改页眉页脚. Make 页眉页脚 -> 导入导出"
- Done:
  - [x] `Services/ReceiptBrandingStore.cs`: `ExportConfigJson()` / `TryParseConfigJson(json)` / `ImportConfig(export)` + new `BrandingExport` DTO — self-contained JSON export includes the settings (header/footer XAML per language, logo placement) plus the logo image as base64, so import restores the logo file too (writes to `logo.<ext>`, clears any stale logo file first).
  - [x] `MainWindow.xaml`: added a third nested submenu under `Toolbar.ImportExport`, reusing `Toolbar.HeaderFooter` as the label, with Export/Import entries (mirrors 量身项目设置/本地数据库).
  - [x] `MainWindow.xaml.cs`: `OnExportBrandingClick`/`OnImportBrandingClick` — SaveFileDialog/OpenFileDialog, invalid-JSON warning dialog, Yes/No overwrite confirmation before import (mirrors measurement-terms handlers), status bar reporting.
  - [x] `Languages.xml` (zh-CN + en-US): `Status.Export/ImportBrandingSucceeded/Failed`, `Status.ImportBrandingInvalid`, `ImportExport.BrandingConfirm`.
- Notes: build succeeded 0 warnings / 0 errors; SonarQube clean on both changed files. No DB/schema change.

### 2026-07-25 — Import/Export menu (量身项目设置 JSON + local database)  [DONE]
- Ask: "Add a new tab on navigation called Import/Export under the local configuration. hover on the option, we have 量身项目设置, hover on it, it expand two options Export or Import. Clicking on Export, it will export 量身项目设置 as json, while I import, it can be recover the configuration from it. we have 本地数据库, also have export and Import feature. Analyze the whole project and implement both features."
- Done:
  - [x] `MainWindow.xaml`: new top-level `本地配置` submenu **导入/导出** (`Toolbar.ImportExport`), containing two nested menus mirroring the existing entries — **量身项目设置** (reuses `Toolbar.MeasurementTerms`) → 导出/导入, and **本地数据库** (reuses `Toolbar.LocalDatabase`) → 导出/导入.
  - [x] `Services/MeasurementTermsService.cs`: `ExportConfigJson()` (indented JSON of the current config), `TryParseConfigJson(json)` (static, returns null on invalid/corrupt JSON instead of throwing), `ImportConfig(config)` (replaces `Terms`/`Garments`, re-runs `MergePredefined` so an export from an older app version still gets today's predefined ids/gender classifications, then persists + raises `ConfigChanged`).
  - [x] `Data/DatabasePathProvider.cs`: `ExportDatabaseTo(targetPath)` and `ImportDatabaseFrom(sourcePath)` (both call `SqliteConnection.ClearAllPools()` first to release any pooled file handles before copying). Import auto-backs-up the current `orders.db` to `orders.db.bak-<timestamp>` alongside it before overwriting, and syncs the `-wal`/`-shm` sidecar files (deletes stale ones, copies matching ones from the source).
  - [x] `MainWindow.xaml.cs`: 4 new handlers (`OnExportMeasurementTermsClick`/`OnImportMeasurementTermsClick`/`OnExportDatabaseClick`/`OnImportDatabaseClick`) using `Microsoft.Win32.SaveFileDialog`/`OpenFileDialog` (matching the existing `CustomMadeServiceWindow`/`ReceiptBrandingWindow` pattern). Both imports show an explicit Yes/No confirmation (`MessageBoxImage.Warning`) before overwriting, since both are destructive; DB import also reloads the order grid (`_viewModel.LoadOrdersCommand.Execute(null)`) afterward. All failures/successes report through the existing `_viewModel.StatusMessage` status-bar pattern.
  - [x] Languages.xml (zh-CN + en-US): `Toolbar.ImportExport/Export/Import`, `Status.Export/ImportMeasurementTerms{Succeeded,Failed}`, `Status.ImportMeasurementTermsInvalid`, `Status.Export/ImportDatabase{Succeeded,Failed}`, `ImportExport.MeasurementTermsConfirm`, `ImportExport.DatabaseConfirm`.
- Notes: build succeeded 0 errors / 0 warnings. No DB schema change. Measurement-terms import validates JSON shape before touching anything (invalid file → warning dialog, no changes made). Database import is the more dangerous of the two — confirmed via dialog AND auto-backed-up, but a full app restart is still the safest way to guarantee every open view reflects the swapped data (not enforced, just recommended if anything looks stale after import).

### 2026-07-25 — Cancelled/returned refund state (UI + receipt + editor lock)  [DONE]
- Ask: "UI: gray out Cancelled/Returned records (lightest gray) and Completed records (a bit darker). Receipt: colour the balance status (已结清（已取货）green / 已结清（未取货）light green / 未结清 orange / 已退款或部分退款 red). Business logic for 已取消/已退货: in edit order, changing status to 已取消/已退货 sets 余额状态 to 已退款或部分退款, locks all services incl. 当前服务尾款已结清, marks all checkboxes (incl. 已取货) red with a stroke across the whole checkbox (not applicable) — but service details (e.g. custom measurement records) stay viewable. In the receipt, remove 剩余尾款 and show 余额状态 = 已退款或部分退款."
- Done:
  - [x] `Order.IsRefunded` (Cancelled/Returned) + `BalanceStatusKind` enum + `Order.PaymentStatusKind` (single source of truth: Refunded / Outstanding / ClearedPickedUp / ClearedNotPickedUp).
  - [x] Languages.xml (both blocks): `Payment.Status.Refunded` = 已退款或部分退款 / "Refunded or Partially Refunded".
  - [x] `OrderPaymentSummaryConverter` Status mode now maps `PaymentStatusKind` → label (so list, detail panel and receipt all show 已退款或部分退款 for cancelled/returned).
  - [x] MainWindow list: added `IsRefunded` DataTrigger (lightest gray #C3C9CF / opacity 0.5); kept `IsPickedUp` trigger (darker #9AA3AB / 0.7) for completed.
  - [x] Receipt (`AddReceiptTotals`): omit 剩余尾款 line when `IsRefunded`; colour balance status via new `ReceiptStatusLine` + `BalanceStatusBrush` (green/light green/orange/red).
  - [x] OrderEditWindow.xaml: `NotApplicableCheckBox` style (red box + red strikethrough label + red line across the whole control).
  - [x] OrderEditWindow.xaml.cs: `_isRefunded` field; `OnStatusChanged` toggles refund lock; `SetServiceControlsEnabled`/`ApplyRefundLockState`/`ApplyNotApplicableCheckboxStyle`; `RefreshPricingLocks` + `UpdateBalanceStatusDisplay` + PickedUp enabling respect `_isRefunded`; refunded style also applied when opening an already-cancelled/returned order (read-only). Customer fields + custom-made records list stay usable so measurements remain viewable.
- Notes: build succeeded 0 warnings / 0 errors. Balance status is computed (no DB change). Extracted `UpdateBalanceStatusDisplay` to keep `RefreshPaymentSummary` cognitive complexity ≤15. Source English-only; non-English only in Languages.xml.

### 2026-07-25 — Custom-service flag column + measurement printing  [DONE]
- Ask: "1. Remove last modified date time from the main records section; show it in order details (order still ordered by last modified). 2. UI: wider Final balance status column + wider Order number column. 3. Replace last modified column with a flag 定制服务: no→无, yes→有 with a second bracketed row (garment names) e.g. 有 /(西装、衬衣), centered, wrappable. 4. Printing: if the order has 定制服务, add under 本地配置>Print: 打印量身尺寸 and 打印小票和所有尺寸 (hide when no custom service); same on right-click menu. 5. Clicking either pops a small window asking language + unit for measurements; this is a PRINT method (not save-to-PDF)."
- Done:
  - [x] Languages.xml (zh-CN + en-US): `Order.Fields.CustomMadeFlag`, `CustomMade.Flag.Yes/No`, `Toolbar.PrintMeasurements`, `Toolbar.PrintReceiptAndMeasurements`, `MeasurePrint.Title/LanguagePrompt/UnitPrompt/Print/Cancel`.
  - [x] `Order.HasCustomMadeService` [NotMapped] — true when any custom-made record has a garment with a cm/inch measurement value.
  - [x] `Services/CustomMadeMeasurementReader` — `GetGarmentNames` (distinct, order-preserving) + `BuildSections` (per-garment term/value rows, unit-aware; per-garment extracted to `BuildGarmentSection` to keep cognitive complexity ≤15).
  - [x] `Converters/CustomMadeServiceFlagConverter` — ConverterParameter `Flag` (有/无) / `Names` (bracketed garment names, zh sep 、 / en sep ", ") / `NamesVisibility`.
  - [x] MainWindow.xaml: removed LastModified column; added centered wrappable 定制服务 CellTemplate column; widened OrderNumber 150→200 and BalanceStatus 140→180; added LastModified to detail panel (between OrderDate and Status); added print menu items (toolbar Print submenu + context menu) gated `Visibility` on `SelectedOrder.HasCustomMadeService` via BoolToVisibility.
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
  - New `Views/MeasurementTermsWindow.xaml(.cs)`: 3-column drag-drop mapping window (left garments / center assigned / right all props), modern styles + Segoe MDL2 icons, inline edit + lock + alt-language + delete + Add Garment/Measurement. Launched from 本地配置 menu.
  - `Models/CustomMadeServiceRecord.cs`: added `List<GarmentMeasurement> Garments` (+ `GarmentMeasurement`, `MeasurementValue`), kept legacy Jacket*/Shirt* fields.
  - `Views/CustomMadeServiceWindow.xaml(.cs)`: replaced static Jacket/Shirt grid with garment ToggleButton selector + dynamically generated per-garment measurement cards; cache-based cm/in dual-unit source of truth (`_valueCache`, `_termEditors`); unit switch persists+reapplies; language change rebuilds selector/cards; seeds from legacy fields for old records; `BuildGarmentsIntoRecord` writes Garments on save; PDF export iterates all selected garments/props (`BuildPdfGarmentSections`).
  - `Converters/CustomMadeRecordSummaryConverter.cs`: summary lists selected garment names from `Garments` (resolved via service), falls back to legacy fields.
  - `MainWindow.xaml(.cs)`: 测量术语 (Measurement Terms) MenuItem under 本地配置 → opens MeasurementTermsWindow.
  - `Languages.xml` (zh-CN + en-US): `Measure.Term.*` (21), `Measure.Garment.*` (7), `Toolbar.MeasurementTerms`, `MeasureTerms.*`, `TermLanguage.*`, `Measure.SelectGarments`, `Measure.NoGarmentSelected`.
- Notes: build succeeded 0 errors / 0 warnings. Source code English-only; non-English strings only in Languages.xml. No DB migration (Garments serialized into existing `Order.CustomMadeRecordsJson`).


### 2026-07-24 — Payment UI beautify / de-nest (OrderEditWindow)  [DONE]
- Ask: "Beautify the Payment components. currently the sections are so nested, organize and categorize them well."
- Done (XAML only, `Views/OrderEditWindow.xaml`):
  - Added reusable `Window.Resources` styles: `SectionCard`, `SummaryCard`, `SectionHeading`, `PaymentCard`, `PaymentTitle`, `StepLabel`, `MethodRadio`, `StepDivider`, `AccentBar`.
  - Converted all top-level section borders (service-type selector, Alterations / CustomMade / ReadyMade panels, Notes) to the white rounded `SectionCard`; totals summary uses `SummaryCard`; headings use `SectionHeading`.
  - Rebuilt the 3 payment sub-cards with a colored accent bar + title header, `StepLabel`-styled deposit/final method labels, `MethodRadio`-styled radios, and a `StepDivider` at the top of each `FinalBlock` so 定金 (deposit) and 尾款 (final) read as two clear steps.
  - Preserved every `x:Name` and event handler (no code-behind changes) — the divider lives inside FinalBlock so it only shows with the final step.
- Notes: build succeeded 0 warnings / 0 errors.

### 2026-07-24 — Currency: per-order → global app setting  [DONE]
- Ask: "Moving the currency setup from record base into global base... small business should rely on the setup globally, no complicated logic. Currency setup moving to local configurations menu bar."
- Decisions (confirmed with user): keep the `Orders.CurrencyType` DB column but ignore it (no migration); offer CAD/USD/CNY; currency entry lives directly under 本地配置 (sibling of 添加或更改页眉页脚).
- Done:
  - New `Services/CurrencySettingService.cs`: singleton `Instance` (INotifyPropertyChanged) with `Current` (CurrencyType), `Symbol` (￥ for CNY else $), `SetCurrency` + `CurrencyChanged`; persists to `currency-setting.json` under LocalAppData (mirrors LanguagePreferenceStore).
  - New `Views/CurrencySettingWindow.xaml(.cs)`: small dialog (currency ComboBox + Save/Cancel) launched from 本地配置.
  - `MainWindow.xaml`: added `货币设置` MenuItem under 本地配置 (Click=OnCurrencySettingClick); removed the per-order 货币 detail row.
  - `MainWindow.xaml.cs`: `OnCurrencySettingClick` opens dialog and reloads orders on change; receipt symbol + currency line now use `CurrencySettingService.Instance`.
  - Converters `CurrencyAmountConverter` (dropped 2nd currency value/`ParseCurrency`) and `OrderPaymentSummaryConverter` now read the global symbol.
  - `OrderEditWindow.xaml(.cs)`: removed the currency panel/`CurrencyBox` and all its wiring (Initialize/Refresh/CreateCurrencyItem/GetSelectedCurrencyType), dropped `CurrencyType` from `OrderSaveData`/`ApplyEditableFields`; `FormatCurrency` now static using global symbol.
  - `Languages.xml`: added `Toolbar.CurrencySetting` (货币设置 / Currency Setup) and `Currency.Title` / `Currency.Prompt` to both blocks.
- Notes: build succeeded 0 warnings / 0 errors. Per-order CurrencyType column retained but unused; global money bindings refresh on next order load.

### 2026-07-24 — Application Menu: 本地配置 dropdown  [DONE]
- Ask: "Application Menu update — 1. Add 本地配置 auto-dropdown on the navigation. Move 页眉页脚 to the item, update wording to 添加或更改页眉页脚. Add 本地数据库 as another auto expansion. Move 复制数据库路径, 定位数据库文件 and 打开数据库目录 into it."
- Done:
  - `MainWindow.xaml`: replaced the standalone 页眉页脚 button and the three data buttons (CopyDataPath / RevealDataFile / OpenDataFolder) with a `Menu` → top-level `本地配置` MenuItem containing `添加或更改页眉页脚` (Click=OnEditBrandingClick) and a nested `本地数据库` submenu (auto-expand) holding the three DB items (reused OnCopyDataPathClick / OnRevealDataFileClick / OnOpenDataFolderClick — no code-behind change).
  - `Languages.xml`: reworded `Toolbar.HeaderFooter` 页眉页脚→添加或更改页眉页脚 / "Header & Footer"→"Add or Change Header & Footer"; added `Toolbar.LocalConfig` (本地配置 / Local Configuration) and `Toolbar.LocalDatabase` (本地数据库 / Local Database) to both blocks.
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
    cleared checkbox live relabels 修改定制记录 → 查看定制记录 (prev only reacted to
    whole-order read-only).
  - `CustomMadeServiceWindow.xaml`: added the implicit `TextBox` read-only style
    (copied from `OrderEditWindow.xaml`) — `IsReadOnly=True` → Background `#F0F0F0`,
    Foreground `#808080`, so view-mode boxes gray out consistently with the order
    editor. (Radios/document buttons already gray via `IsEnabled=false`.)
- Notes: `dotnet build` → 0 errors; SonarLint clean. Files: `OrderEditWindow.xaml.cs`,
  `CustomMadeServiceWindow.xaml`.

### 2026-07-24 — Custom-made record: open read-only (查看定制记录) when section balance cleared  [DONE]
- Ask: "BUG Fix — For Custom made service, if the current payment final balance is cleared, then 1. 修改定制记录 -> 查看定制记录; 2. all records fields should be locked, including the upload image area."
- Done: `OrderEditWindow.OnEditCustomMadeRecordClick` now computes
  `recordReadOnly = _isReadOnly || CustomMadeBalanceClearedCheck.IsChecked is true`
  (same gate as `RefreshPricingLocks`) and passes it as `isReadOnly` to
  `CustomMadeServiceWindow`, and uses it for the open-validation skip + view-only
  ShowDialog path. Reuses the window's existing `ApplyReadOnlyMode`
  (title → `OrderEdit.ViewCustomMade` "查看定制记录", all boxes/radios read-only,
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
    the UI live; styled full-width "进入系统 / Enter System" button confirms.
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
- Follow-up (2026-07-23): per new skill rule (§5 dropdown default), the category dropdown now defaults to the **first** option 服装修改/Garment Adjustments — `SelectedIndex = 0` on the new-order path and a fallback to the first item on edit-load when the stored value matches no option.

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
  - [x] T2 vertical-center 定制方式 radios
  - [x] T3 move tax to custom-made SECTION level (Order.CustomMadeTaxRate + column guard, editable CustomMadeTaxBox, drop record tax)
  - [x] T4a Enter opens editor (custom-made list + main orders grid)
  - [x] T4b ESC closes popups (IsCancel on Cancel buttons)
  - [x] T4c deposit box clears 0 on focus, restores on blur
  - [x] T5a money regex on pricing inputs (2 decimals)
  - [x] T5b email validation + red message beneath EmailBox
  - [x] T5c phone validation + red message beneath PhoneNumberBox
  - [x] T5d measurements regex (start digit, one dot, optional trailing +/-)
  - [x] SonarLint before build, then build; update context.md
- Notes: **T1** Languages.xml both blocks (只量身/定制量身, Measure Only/Full Custom); `CustomMadeServiceWindow.xaml` radios reordered (Full Custom first, `IsChecked="True"`); `CustomMadeServiceRecord.ServiceMode` default + editor `InitializeMode(... ?? CustomFromScratch)`. **T2** mode `StackPanel` + both radios `VerticalAlignment="Center"`. **T3** `Order.CustomMadeTaxRate` (+ `App.xaml.cs` column guard `CustomMadeTaxRate TEXT NULL`), `CustomMadeSubtotal`/`CustomMadeTotal` now mirror Alteration/Clothing section-tax pattern; `OrderEditWindow` `CustomMadeTaxText`→editable `CustomMadeTaxBox` (13% default, card-driven enable via `RefreshCustomMadeTotals`), persisted in `ApplyPaymentFields`; per-record Tax/SumTotal UI removed from `CustomMadeServiceWindow` (record `TaxRate` left null for back-compat). Legacy orders show no custom-made tax until re-saved (accepted, consistent w/ other sections). **T4a** `OnCustomMadeRecordsKeyDown` + `MainWindow.OnOrderRowKeyDown` (Enter → edit). **T4b** `IsCancel="True"` on both Cancel buttons. **T4c** `RegisterDepositBox` + `OnDepositBoxGotFocus`/`LostFocus` (clears leading 0 on focus w/ `_syncingPayment` guard, restores "0" on blur). **T5a** `DecimalInputPattern` → `^\d*(\.\d{0,2})?$`; money `PreviewTextInput`+paste filters. **T5b/5c** `EmailPattern`, `IsValidPhone` (regex `^\+?[\d\s\-().]+$` + 7-15 digit count), `PhoneErrorText`/`EmailErrorText` red inline blocks, `LostFocus` validation + block in `TryValidateForSave`; keys `OrderEdit.Validate.EmailInvalid`/`PhoneInvalid` both blocks. **T5d** `MeasurementInputPattern` `^(\d+(\.\d*)?[+-]?)?$` on all 8 measurement boxes (`PreviewTextInput`+paste). SonarLint clean (reworded 2 comments to dodge S125 false positives); build succeeded 0 errors.

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

### 2026-07-23 — "已取货 / Picked up" quick-complete checkbox  [DONE]
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
