# Cameyware Order

A multi-shop, multi-user desktop order-management application for bespoke tailoring. It records
customer orders across three service lines, tracks the money owed and received on each one
independently, captures made-to-measure body measurements, and prints receipts and measurement
sheets.

Windows desktop app: **WPF on .NET 8**, with all data stored locally.

---

## Latest release

### v2.0.1 — 2026-07-29 (hotfix)

- **Fixed: an order whose deposit covered its whole total could not have its balance re-opened.**
  Pay the full amount as a deposit and mark it received, and the service auto-cleared and the
  "clear all final balances" tick came on — both correct — but the tick could not then be removed.
  It sprang back the moment anything recalculated, so the order was stuck settled. Unticking now
  unticks every service and stays unticked, even where the outstanding balance is zero.
- **Fixed: the final-balance payment method could not be chosen on a fully-deposited service.** It
  is selectable now, so how a zero balance was settled can still be recorded.
- **The price breakdown shows what each portion COSTS beside what has been TAKEN for it.** A
  deposit-due and a balance-due figure appear from the start; their received counterparts appear
  only once that portion is confirmed, so the pair reads as a charge and then as a receipt.
- **Wording:** 实收定金 / 实收尾款 are now 已收定金 / 已收尾款.
- **Orders record who served them.** The crew member who last saved an order is stamped onto it,
  printed on its receipt and shown in the order detail panel beside the last-modified time. The
  name is stored *as it read at the time* — a receipt is a historical document, so it must not
  change the day somebody is renamed, or blank the day their account is removed. Orders saved
  before this release have no name recorded and omit the line entirely rather than showing an
  empty one.

Upgrading adds one nullable column to the orders table on first launch. Nothing is asked of the
user, and no existing value changes.

Quality gates: build **0 warnings / 0 errors**, **921 assertions across 20 harnesses**, all passing.

### v2.0.0 — 2026-07-29

Money became a property of the **order** rather than of the shop, and the languages a shop runs in
now decide the currencies it can take. A major version because both changes reach the database: a
new column, and a one-time repair of every existing order.

- **Five languages — 简体中文, English, Français, Español, 日本語.** Español and 日本語 were added by
  dropping one file each into `Settings/System/Languages`, which is what that folder promised.
  Japanese is the second CJK language and the first to prove that **punctuation is data**: it joins
  a list with `、` and no trailing space, brackets fullwidth with `（）` and quotes with `「」` —
  three shapes no other shipped language uses, none of which any code decides.
- **A shop accepts one or more currencies, and every order records the one it was priced in.**
  Previously each amount was rendered in whatever the shop was set to *today*, so the first branch
  to take a second currency would have reprinted its whole history in it — a ￥1,695 order as
  "$1,695.00". An order now keeps its own currency for good, including after the shop stops
  accepting it.
- **The currencies on offer come from the installed languages.** Each language file declares its
  market's own — `CNY` for 简体中文, `EUR` for Français and Español, `JPY` for 日本語, and `CAD,USD`
  for English, which is the home market and lists both with CAD first. Adding a language brings its
  currency with it.
- **Yen prints as whole yen.** JPY has no minor unit, so `¥1,695.00` is wrong in the same way a
  wrong symbol is. Symbol and decimal places are one fact about a currency and are formatted
  together.
- **Languages and currencies moved into a panel of their own**, reached from a link in Shop
  Settings: the languages a shop runs in on the left, the currencies each of them brings on the
  right. They are one decision, and the shop form was long enough already.
- **Every window fits the screen it opens on.** The order editor declared a minimum height of 900px
  against a laptop work area of 752, which put its Cancel/Save footer below the desktop edge with no
  way to drag it into view — saving an order was impossible on that machine. Windows now scale down
  proportionally to the screen they open on, and never scale up.

**Upgrading from v1.0.0 is automatic.** The first launch adds the new column and stamps every
existing order with its shop's currency. Nothing is asked of the user, and nothing is lost.

Quality gates for this release: build with **0 warnings / 0 errors**, every issue the SonarLint
analyzer raised on a changed file cleared, and a scratchpad harness suite of **886 assertions across
19 harnesses**, all passing.

> Still not versioned in the build — the assembly carries no `<Version>` and the repository has no
> git tag. Both are one-line additions if you want the release marked outside this file too.

---

## Earlier releases

### v1.0.0 — 2026-07-28

The first marked release. What it covers:

- **Multi-shop.** Every branch carries its own name and address (per language), contact details,
  tax registration number, currency, payment/tax rules, receipt-number format, measurement terms,
  product catalogue and receipt branding. Orders are confined to the open shop at the database
  level, not by a `WHERE` clause somebody has to remember.
- **Accounts and roles.** An administrator, or per-shop memberships carrying role(s), activation,
  start date and shift. The same person can be a manager in one branch and staff in another;
  suspending them at one never costs them the other. Managers run a roster for their own shop;
  administrators manage every account, and can **sign in as** any of them to see the application as
  that person does.
- **Three languages — 简体中文, English, Français — and each shop chooses which it runs in.**
  Managers and staff switch between the languages their branch installs; an administrator sees them
  all. Adding a fourth language is dropping one file into `Settings/System/Languages`.
- **Per-portion money.** Deposit and final balance each have their own payment method *and* tax
  rate, and whether a method is taxed at all is a shop rule. One order can mix all three service
  lines, each settled on its own schedule.
- **Printing.** Receipts and measurement sheets, both with a configurable header, footer and logo,
  and a generated letterhead when no custom header is set. Measurement sheets export to PDF in the
  language and unit chosen at print time.
- **Import / export**, including a one-click, self-contained backup of every local setting and all
  data — enough to move an installation to another machine.

Quality gates for this release: build with **0 warnings / 0 errors**, **0 SonarQube findings**, and
a scratchpad harness suite of **731 assertions across 17 harnesses**, all passing.

---

## What it does

An order is made up of up to three independently priced **service sections**:

| Section | Covers | Priced by |
| --- | --- | --- |
| Alterations | Garment adjustments and other alteration work | A single service price |
| Custom-made | Bespoke tailoring, with per-garment measurement records | Sum of its records |
| Ready-made | Off-the-rack clothing and accessories | Line items |

Each section carries its own subtotal, deposit, tax rates, payment methods and settlement state, so
one order can mix all three and each is settled on its own schedule.

Around that core the app provides:

- **Order list** with search, status filter, paging (including left/right arrow keys), sortable
  columns and an adjustable row font size. Every column is one line, ellipsized — rows never change
  height.
- **Receipt printing** and **measurement-sheet printing**, both with a configurable header/footer
  and logo, plus PDF export of measurements.
- **Measurement Terms** — a configurable dictionary of body measurements and garment types, mapped
  to each other through a drag-and-drop editor, per shop.
- **Product catalogue** — the ready-made categories each shop sells, per shop.
- **Document attachments** on custom-made records: handwriting receipts, fabric samples, photos and
  other images.
- **Import / export**, including a one-click backup of every local setting and all data.
- **Full localization** — every piece of UI text is translated at runtime.

---

## Getting started

### Requirements

- Windows
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (the project targets
  `net8.0-windows`)

### Build and run

```powershell
dotnet build CameywareOrder.csproj
dotnet run --project CameywareOrder.csproj
```

> **The running app locks its own executable.** A rebuild fails with a file-in-use error unless you
> stop it first:
>
> ```powershell
> Get-Process -Name CameywareOrder -ErrorAction SilentlyContinue | Stop-Process -Force
> Start-Sleep -Milliseconds 400
> dotnet build CameywareOrder.csproj
> ```

### First sign-in

A fresh installation seeds a single administrator: **`admin` / `admin`**. Change the password
immediately — anyone who can read this file can sign in otherwise.

The sign-in screen deliberately never names an account, and gives the same message for an unknown
user name as for a wrong password, so it cannot be used to discover who has access. That is why the
initial credential has to be communicated here rather than shown on screen.

After signing in you choose a shop. On a single-shop installation, staff and managers skip the
picker entirely.

---

## Where your data lives

Everything the application **writes** lives under `%LOCALAPPDATA%\CameywareOrder\`:

| Path | Contents |
| --- | --- |
| `orders.db` | SQLite database of shops, orders and line items (plus `-wal` / `-shm` sidecars) |
| `Documents\CustomMade\` | Images attached to custom-made records |
| `Branding\` | Receipt header/footer settings and the logo image |
| `Config\credentials.json` | Accounts, roles and per-shop memberships |
| `Config\currency-setting.json` | Selected currency |
| `Config\language-preference.json` | Language used by the screens shown before a shop is open |
| `Backups\` | Safety copies taken before an import |
| `measurement-terms-<shop>.json` | Measurement terms, garment types and mappings, per shop |
| `product-catalog-<shop>.json` | Ready-made categories, per shop |

Nothing is sent anywhere — the app is entirely local.

Accounts live **outside** the database on purpose: importing a database replaces that file wholesale
and must not wipe everyone's sign-in.

### Shipped configuration

`Settings\System\` next to the executable is the other half, and the opposite kind of thing:
read-only, versioned in git, and replaced wholesale by an upgrade.

| Path | Contents |
| --- | --- |
| `Languages\<code>.lang.xml` | One document per language — the entire UI string table |
| `Defaults\app-defaults.json` | Default language, backup retention count |

Ask which kind a new file is before choosing where it goes: user data must survive an upgrade,
shipped configuration must be replaced by one.

### Backups and moving to another PC

**Local Configuration → Import/Export → Global Settings → Export** writes a single zip containing
the database, every attached image, the measurement terms, the branding (logo included), the
currency and the language.
Importing that file on another machine restores the lot.

Every export is self-contained by design, so a restore never leaves a dangling image reference.
Individual exports are available too, for the database, the measurement terms and the header/footer
separately.

Imports are destructive and always ask for confirmation first. The database import automatically
backs up the current `orders.db` and `Documents\` folder before replacing them. After a database or
global import, restart the app so every open view reflects the new data.

---

## Project layout

```
Models/          Domain entities: Shop, Order, OrderItem, custom-made records, measurement terms
Data/            EF Core DbContext, design-time factory, database path + import/export
Services/        Authentication, shop context, shop languages, measurement terms, currency,
                 branding, product catalogue, document storage, backup packaging, print layouts
ViewModels/      MainViewModel (list, paging, search, sorting, copy/delete) + RelayCommand
Views/           All windows other than the main one (order editor, measurements, shop picker,
                 user management, store members, settings)
Converters/      IValueConverter / IMultiValueConverter types used by the XAML
Localization/    LocalizationService and the language preference store
Configuration/   Locates shipped settings and the per-installation data folder
Themes/          AppTheme.xaml — the application's single visual language
Animations/      The shared panel open/close transition
Controls/        Attached behaviours for things a style cannot express, and WindowFitting —
                 which scales any window down to fit the screen it opens on
GraphQL/         Query and Mutation types for the embedded API
Migrations/      EF Core migrations and the model snapshot
Settings/        Shipped, read-only configuration: language files and app defaults
Assets/          Application icon and imagery
```

`MainWindow.xaml(.cs)` holds the order list, the detail panel and the printing code.

---

## Architecture notes

### Shops

Every order belongs to exactly one shop. `AppDbContext` enforces that in two places rather than at
call sites: a query filter confines every read to the open shop, and `SaveChanges` stamps the shop
onto every new order. Anything stored outside the database that belongs to a shop is keyed on
`Shop.PublicId`, never the local autoincrement `Id`, because whole databases move between machines.

### Authorization

Per shop, and re-evaluated whenever the shop changes — the same person can be a manager in one
branch and staff in the next. Decisions go through named capabilities on `AuthenticationService`
(`CanConfigureShop`, `CanManageUsers`, …), never role comparisons in the UI. Chrome is hidden rather
than disabled, and every hidden action re-checks its capability in the handler: a hidden menu is a
fact about the UI, not a permission.

### Persistence

EF Core 8 over SQLite. Alongside the EF migrations, `App.xaml.cs` runs tables of **idempotent
runtime column guards** at startup (`ALTER TABLE ... ADD COLUMN` behind an existence check), so an
existing database upgrades in place when a new field is added. When you add a persisted property,
add a matching guard — and remember there are two tables to keep in step, one for `Orders` and one
for `Shops`.

### Money

All per-section money is derived from one function, `Order.CalculateSectionPayment`, which returns
an immutable `SectionPayment`. Both the model and the live order editor call it, so the amounts on
screen and the amounts recomputed from a saved order can never disagree.

Four rules are worth knowing:

- **Tax applies per payment portion, not per section.** The deposit and the final balance each have
  their own payment method *and* their own tax rate. A card deposit at 5% and a card balance at 7%
  are perfectly valid on the same service.
- **Whether a method is taxed at all is a SHOP rule; the rate is stored on the ORDER.** So changing
  a shop's tax rules never silently re-prices an order that was already saved.
- **Deposits and balances are pre-tax amounts.** Tax is added on top of whichever portion incurs it.
- **The currency is a property of the ORDER, not of the shop.** Which currencies a shop accepts is a
  statement about today; what an order was priced in is a fact about when it was taken. Every amount
  on screen and on a receipt is rendered from `Order.CurrencyType`, so a branch that starts taking a
  second currency never re-denominates its own history.

### Derived state

Status-driven state is computed, never stored in its own column — "picked up" *is*
`OrderStatus.Completed`, and "refunded" *is* Cancelled or Returned. The balance-status indicator
comes from a single `Order.PaymentStatusKind`, which the list, the detail panel, the receipt and the
editor each render in their own way.

### Embedded GraphQL API

The app hosts a Hot Chocolate GraphQL server in-process, preferring `http://localhost:5050/graphql`
and falling back to a free port when that one is taken. Nothing in the UI reads through it — it
exists for external callers — so a failure to bind degrades the API and never the application.

---

## Localization

`Settings\System\Languages\` holds **one document per language**, each with an identical set of
keys. Languages are *discovered*, not registered: adding one is dropping a file in. The `code`
attribute inside the file is its identity — the file name is only a convention.

A language file also declares **the currencies of its market**, which is how a shop's currency
options are decided:

```xml
<Text key="Currency.Codes">CAD,USD</Text>   <!-- en-US: the home market, CAD listed first -->
<Text key="Currency.Codes">CNY</Text>       <!-- zh-CN -->
```

So a new language brings its currency with it. The one constraint is that `CurrencyType` still
bounds what can be *stored* — its values are persisted as integers on orders and shops — so a code
that enum cannot name is ignored rather than guessed at. Adding a genuinely new currency is one line
there and one in the symbol table.

Bind in XAML:

```xml
Text="{Binding Source={x:Static loc:LocalizationService.Instance}, Path=[Order.Fields.OrderNumber], Mode=OneWay}"
```

Read in code:

```csharp
_localization["Order.Fields.OrderNumber"]
_localization.Format("Status.LoadedSummary", count)
```

When adding a string:

1. Add the key to **every** language file, in the same relative position. `LocalizationService`
   computes the gap between files, and the test suite fails on it — otherwise a missing key silently
   falls back to the default language and the screen looks fine in testing.
2. Put the **whole line shape** in the translation, not just the words — Chinese uses fullwidth
   `（）：、` where English uses `(): ,`, so concatenating punctuation in C# around a translated
   fragment produces wrong output in one of the languages.
3. Punctuation that varies by language is *data*: join lists through `LocalizationService.JoinList`
   / `JoinFragments` rather than reaching for a separator yourself.
4. Never hard-code non-English text in `.cs` or `.xaml`. Source code, comments and documentation are
   English-only; translated text belongs in the language files.

To rename a label everywhere it appears, change the key's *value*. Add a new key only when you need
a genuinely new label.

> A key that appears unused may not be: around thirty are composed at runtime, as
> `$"Measure.Term.{id}"` and the like. Deleting one is silent — the lookup returns the key itself and
> the screen reads "Measure.Term.waist".

---

## Contributing

Development conventions — localization, the money model, converter-driven XAML, printing, reentrancy
guards, and the pre-build quality gates — are documented in
[`AgentSkills/wpf-dev/SKILL.md`](AgentSkills/wpf-dev/SKILL.md).

That folder also holds the working state of the project:

- [`Architecture.md`](AgentSkills/wpf-dev/Architecture.md) — component map
- [`context.md`](AgentSkills/wpf-dev/context.md) — current decisions and known gotchas
- [`RefinedTODO.md`](AgentSkills/wpf-dev/RefinedTODO.md) — the condensed working history; **read this one**
- [`TODO.md`](AgentSkills/wpf-dev/TODO.md) — the full, unabridged development record

Read those before making changes; they explain why several non-obvious things are the way they are.

Verify every change set with a clean build:

```powershell
dotnet build CameywareOrder.csproj -v quiet --nologo
```

Expect `Build succeeded. 0 Error(s)`. A change is not finished until the changed files are also
free of SonarQube findings.
