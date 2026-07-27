# Cameyware Order

A bilingual (Simplified Chinese / English) desktop order-management application for a
bespoke tailoring shop. It records customer orders across three service lines, tracks the
money owed and received on each one independently, captures made-to-measure body
measurements, and prints receipts and measurement sheets.

Windows desktop app: **WPF on .NET 8**, single-user, with all data stored locally.

---

## What it does

An order is made up of up to three independently priced **service sections**:

| Section | Covers | Priced by |
| --- | --- | --- |
| Alterations | Garment adjustments and other alteration work | A single service price |
| Custom-made | Bespoke tailoring, with per-garment measurement records | Sum of its records |
| Ready-made | Off-the-rack clothing and accessories | Line items |

Each section carries its own subtotal, deposit, tax rates, payment methods and settlement
state, so one order can mix all three and each is settled on its own schedule.

Around that core the app provides:

- **Order list** with search, status filter, paging, sortable columns and an adjustable
  row font size.
- **Receipt printing** and **measurement-sheet printing**, both with a configurable
  header/footer and logo.
- **Measurement Terms** — a configurable dictionary of body measurements and garment
  types, mapped to each other through a drag-and-drop editor.
- **Document attachments** on custom-made records: handwriting receipts, fabric samples,
  photos and other images.
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

> **The running app locks its own executable.** A rebuild fails with a file-in-use error
> unless you stop it first:
>
> ```powershell
> Get-Process -Name CameywareOrder -ErrorAction SilentlyContinue | Stop-Process -Force
> Start-Sleep -Milliseconds 400
> dotnet build CameywareOrder.csproj
> ```

On first launch the app asks which language to use; the choice is remembered.

---

## Where your data lives

Everything is stored under `%LOCALAPPDATA%\CameywareOrder\`:

| Path | Contents |
| --- | --- |
| `orders.db` | SQLite database of orders and line items (plus `-wal` / `-shm` sidecars) |
| `Documents\CustomMade\` | Images attached to custom-made records |
| `Branding\` | Receipt header/footer settings and the logo image |
| `measurement-terms.json` | Measurement terms, garment types and their mappings |
| `currency-setting.json` | Selected currency |
| `language-preference.json` | Selected UI language |

Nothing is sent anywhere — the app is entirely local.

### Backups and moving to another PC

**Local Configuration → Import/Export → Global Settings → Export**
(本地配置 → 导入/导出 → 全局设置 → 导出) writes a single zip containing the database, every
attached image, the measurement terms, the branding (logo included), the currency and the
language. Importing that file on another machine restores the lot.

Every export is self-contained by design, so a restore never leaves a dangling image
reference. Individual exports are available too, for the database, the measurement terms and
the header/footer separately.

Imports are destructive and always ask for confirmation first. The database import
automatically backs up the current `orders.db` and `Documents\` folder before replacing them.
After a database or global import, restart the app so every open view reflects the new data.

---

## Project layout

```
Models/          Domain entities: Order, OrderItem, custom-made records, measurement terms
Data/            EF Core DbContext, design-time factory, database path + import/export
Services/        Measurement terms, currency, branding, document storage, backup packaging
ViewModels/      MainViewModel (list, paging, search, sorting, copy/delete) + RelayCommand
Views/           All windows other than the main one (order editor, measurements, settings)
Converters/      IValueConverter / IMultiValueConverter types used by the XAML
Localization/    LocalizationService and the language preference store
GraphQL/         Query and Mutation types for the embedded API
Migrations/      EF Core migrations and the model snapshot
Assets/          Application icon and imagery
Languages.xml    The single source of every user-facing string
```

`MainWindow.xaml(.cs)` holds the order list, the detail panel and the printing code.

---

## Architecture notes

### Persistence

EF Core 8 over SQLite. Alongside the EF migrations, `App.xaml.cs` runs a table of
**idempotent runtime column guards** at startup (`ALTER TABLE ... ADD COLUMN` behind an
existence check), so an existing shop database upgrades in place when a new field is added.
When you add a persisted property, add a matching guard.

### Money

All per-section money is derived from one function, `Order.CalculateSectionPayment`, which
returns an immutable `SectionPayment`. Both the model and the live order editor call it, so
the amounts on screen and the amounts recomputed from a saved order can never disagree.

Two rules are worth knowing:

- **Tax applies per payment portion, not per section.** The deposit and the final balance
  each have their own payment method *and* their own tax rate, and a portion is taxed only
  when that portion is settled by card. So a card deposit at 5% and a card balance at 7% are
  perfectly valid on the same service.
- **Deposits and balances are pre-tax amounts.** Card tax is added on top of whichever
  portion incurs it.

### Derived state

Status-driven state is computed, never stored in its own column — "picked up" *is*
`OrderStatus.Completed`, and "refunded" *is* Cancelled or Returned. The balance-status
indicator comes from a single `Order.PaymentStatusKind`, which the list, the detail panel,
the receipt and the editor each render in their own way.

### Embedded GraphQL API

The app hosts a Hot Chocolate GraphQL server in-process on `http://localhost:5050/graphql`
via the .NET Generic Host, exposing order queries and mutations for future integrations.

---

## Localization

`Languages.xml` at the repository root is the **single source** of every user-facing string.
It holds one block per language, each with an identical set of keys.

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

1. Add the key to **every** language block, in the same relative position.
2. Put the **whole line shape** in the translation, not just the words — Chinese uses
   fullwidth `（）：、` where English uses `(): ,`, so concatenating punctuation in C# around a
   translated fragment produces wrong output in one of the languages.
3. Never hard-code non-English text in `.cs` or `.xaml`. Source code, comments and
   documentation are English-only; translated text belongs in `Languages.xml`.

To rename a label everywhere it appears, change the key's *value*. Add a new key only when
you need a genuinely new label.

> Copies of `Languages.xml` under `bin/`, `obj/` and `publish/` are build output. Only ever
> edit the one at the repository root.

---

## Contributing

Development conventions — localization, the money model, converter-driven XAML, printing,
reentrancy guards, and the pre-build quality gates — are documented in
[`AgentSkills/wpf-dev/SKILL.md`](AgentSkills/wpf-dev/SKILL.md).

That folder also holds the working state of the project:

- [`Architecture.md`](AgentSkills/wpf-dev/Architecture.md) — component map
- [`context.md`](AgentSkills/wpf-dev/context.md) — current decisions and known gotchas
- [`TODO.md`](AgentSkills/wpf-dev/TODO.md) — changelog of completed and open work

Read those before making changes; they explain why several non-obvious things are the way
they are.

Verify every change set with a clean build:

```powershell
dotnet build CameywareOrder.csproj -v quiet --nologo
```

Expect `Build succeeded. 0 Error(s)`.
