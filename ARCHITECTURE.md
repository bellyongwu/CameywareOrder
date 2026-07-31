# CameywareOrder — Architecture & Design

A multi-shop, multi-user desktop order-management application for bespoke tailoring, written in
**WPF on .NET 8**, storing everything locally.

This document records the architecture *and the reasoning behind it*. Where a decision had a
plausible alternative, the alternative and why it was rejected are written down — those are the parts
that get quietly undone later by someone who only sees the result.

**Related documents**
- `README.md` — release notes, written for the shop owner.
- `AgentSkills/wpf-dev/Architecture.md` — the component map, kept for day-to-day navigation.
- `AgentSkills/wpf-dev/context.md` — the failure modes and framework behaviours learned the hard way.
- `AgentSkills/wpf-dev/SKILL.md` — the development conventions and verification discipline.

---

## Table of contents

1. [Stack and shape](#1-stack-and-shape)
2. [Startup, session and composition root](#2-startup-session-and-composition-root)
3. [Authentication and authorisation](#3-authentication-and-authorisation)
4. [The multi-shop model](#4-the-multi-shop-model)
5. [Data and persistence](#5-data-and-persistence)
6. [Localization](#6-localization)
7. [Theme and UI](#7-theme-and-ui)
8. [Validation](#8-validation)
9. [Money, payments and tax](#9-money-payments-and-tax)
10. [Printing and documents](#10-printing-and-documents)
11. [Configuration and file layout](#11-configuration-and-file-layout)
12. [Quality gates and verification](#12-quality-gates-and-verification)
13. [Release history](#12a-release-history)
14. [Cross-cutting principles](#13-cross-cutting-principles)

---

## 1. Stack and shape

### 1.1 Platform

| Concern | Choice |
|---|---|
| UI | WPF, `net8.0-windows`, C# with `Nullable` and `ImplicitUsings` enabled |
| Persistence | EF Core 8 + SQLite, single local file |
| API | Hot Chocolate GraphQL, in-process, via the .NET Generic Host |
| Printing | `FlowDocument` + `PrintDialog`; QuestPDF for the measurements export |
| Analysis | SonarAnalyzer.CSharp, wired into every build |

### 1.2 Shape of the application

Single-process desktop application. No server, no network dependency, no cloud account. Everything —
database, credentials, settings, branding, attached documents — lives on the machine under
`%LOCALAPPDATA%\CameywareOrder`.

The GraphQL server is for **external callers only**; nothing in the UI consumes it. It therefore must
never block startup: the preferred port (5050) is a preference, `ResolveServerPort` falls back to a
free port, and if the bind still fails the application runs without the API.

### 1.3 Deliberate non-goals

- **No user-visible sync or multi-machine story.** Data moves by explicit export/import.
- **No per-order currency conversion.** An order records the currency it was priced in and is never
  re-expressed in another.
- **No retroactive tightening of validation.** New rules apply to new and edited data; see §8.2.

---

## 2. Startup, session and composition root

### 2.1 Composition root

`App.xaml.cs` builds the Generic Host, registers `AppDbContext`, the GraphQL server and the
view-models, runs the startup schema guards, and loads the saved language.

### 2.2 Session flow

```
launch → LoginWindow → shop picker (OpenShopOrSignInAgainAsync) → MainWindow
```

The shop step **loops**. Cancelling the picker signs the user out and returns to sign-in rather than
ending the application: the two steps read as one flow, so "Cancel" on the second means "go back".
`Shutdown()` is reached only when the *login* window is dismissed — the one gesture that
unambiguously means "I am done".

`SignOutAsync` reuses the same loop, so the startup and sign-out paths cannot drift apart.

### 2.3 Ordering rules that are easy to get wrong

- **`ShutdownMode` is relaxed *before* the main window closes.** Otherwise WPF treats that close as
  the end of the application and the login window never appears.
- **The session is revoked *after* the window is down, never before.** Every capability gate reads
  `CurrentUser`; clearing it under a live window leaves administrator-only controls on screen.
- **The previous shop stays bound until the next is chosen.** The GraphQL server is still running and
  calls `ShopContext.RequireCurrent`.

### 2.4 Session states

| State | Window | Session | Shop |
|---|---|---|---|
| Signed out | `LoginWindow` | none | none |
| Working | `MainWindow` | live | bound |
| **Locked** | `LockScreenWindow` | **revoked** | remembered in a local only |
| Switching user | picker | swapped | re-chosen |

---

## 3. Authentication and authorisation

### 3.1 Where accounts live

`credentials.json` under LocalAppData — **outside the database on purpose**. An Import → Database
restore replaces the whole database file, and must not wipe the accounts that would let anyone back
in.

Passwords are **PBKDF2-HMAC-SHA256**, 100,000 iterations, with a per-record salt. The iteration count
is stored **per record**, so the cost can be raised later without invalidating existing credentials.

An unknown user name costs the same time as a wrong password — the hash is computed anyway, against a
random salt — so response time cannot be used to enumerate accounts.

The file carries a **schema version** (currently 4) and upgrades itself on load: flat assignments
folded into memberships (v2), the single display name split into first and last (v3), and a
`ProvisionedAccounts` list that records which seed names have been created, so deleting a seeded
account is permanent rather than undone by the next launch.

### 3.2 Account model

- An account is **either** an administrator (`IsAdministrator` — everything, everywhere, never holds
  a shop membership) **or** holds `ShopMembership`s: one record per shop, carrying role(s),
  `IsActive`, `JoinedOn`, `DeactivatedOn` and an optional shift, keyed on `Shop.PublicId`.
- A person has a `FirstName` and a `LastName`; `PersonName` composes `Full`, `Label` (the name, or
  the login when there is none — never blank) and `Greeting` (the first name).
- `PhoneNumber` and `Email` are **account-level**, not per membership: one person working at two
  branches has one phone and one mailbox.
- The administrator cannot be deleted, renamed or given memberships, and no account can be promoted
  to administrator. `ProvisionSeedAccounts` identifies it by its **flag**, not its name, so "exactly
  one administrator" holds structurally.

### 3.3 Capabilities

`BindShop` supplies the shop that capabilities resolve against:

| Capability | Who |
|---|---|
| `CanCreateShops`, `CanManageUsers`, `CanUseDataTools`, `CanDeleteAccounts` | administrator (installation-wide) |
| `CanConfigureShop`, `CanManageStoreMembers` | administrator, or the open shop's manager |
| `CanChooseAnyLanguage` | administrator — meaning *any shipped* language, not "may switch at all" |

`StrongestRole` takes the **minimum** `UserRole` because the enum is ordered strongest-first.

### 3.4 Sign-in outcomes

`Authenticate` returns a `SignInResult` whose `SignInFailure` distinguishes:

- **bad credentials** — one message for an unknown user name *and* a wrong password, or the dialog
  becomes a user-name oracle;
- **deactivated** — reported distinctly, because the credential *was* right and retyping it will
  never help; the person needs to be told to talk to their manager.

The login window never pre-fills a user name. Signing out is overwhelmingly "somebody else takes
over", and a pre-filled name both announces that an account exists and invites typing the next
person's password against the previous person's account.

### 3.5 Locking a session (v4.1.0)

A lock keeps the **user** and the **shop**; only the password is asked for again.

- Reached by **ESC** on the main window or the toolbar **Lock** button, both through one entry point
  so they cannot drift.
- `AuthenticationService.SignOut()` **is** called before the lock screen appears. A lock that kept
  `CurrentUser` alive would leave every capability gate answering yes behind a screen that looks
  closed. What makes it a lock rather than a sign-out is only that the *shop* is remembered.
- The account and shop id live in **locals for the length of `LockAsync`** — not a field, not a
  setting, not on disk — so nothing about a locked session survives the process.
- **No Cancel.** Closing the window or Alt+F4 signs out. A lock that can be dismissed is not a lock.
- **Only the locking account can unlock.** Another person's correct password is refused, because
  unlocking resumes someone else's shop, role and name on every order saved next. They use *Sign out
  instead*.
- **Access is re-checked on the way back in**, through the same accessible-shops filter the picker
  uses, so a membership revoked while the machine sat locked lands the user at sign-in.

### 3.6 Sign in as another user

The administrator's `SignInAs` hands the session to another account without its password. Gated **in
the service**, not the UI: administrator only, never yourself, never an account delisted by every
shop. It clears the bound shop, since capabilities must not go on resolving against the shop the
administrator had open.

---

## 4. The multi-shop model

### 4.1 Shop context

`ShopContext.Instance` holds the open shop. `AppDbContext` **captures it in its constructor** and
filters `Orders` to that shop — so a cross-shop read through a normal query silently matches nothing.
Anything that must reach across shops says `IgnoreQueryFilters()` explicitly (`ShopAdministration`
does this on every read).

### 4.2 What a shop owns

Name and address **per language**, phone, email, website, tax registration number, currency set, tax
jurisdiction, per-method payment tax rules, receipt numbering, measurement terms, branding, product
catalogue, installed languages.

### 4.3 Shop administration

`ShopAdministration` (static) is the one place the shop-level destructive rules live:

- **Delist / Activate** — sets the existing `Shop.IsArchived` plus a `DelistedOnUtc` audit stamp.
  Reversible and non-destructive, and deliberately *not* behind the confirmation gate: an
  administrator closing a branch for the season should not have to reach for the dangerous tool.
- **Delete** — orders, items, the shop row, and the per-shop files named after its `PublicId`.
- **Reinitialize** — every shop, but accounts, language and global settings are deliberately kept so
  nobody is locked out.

Destructive actions go through `ConfirmDestructiveWindow`: a 10-character phrase generated per dialog
from `RandomNumberGenerator` over an alphabet with **no lookalike pairs** (neither half of O/0, I/1/L,
S/5, Z/2, B/8, G/6, Q/O), typed case-sensitively before either button enables. Copying is allowed on
purpose — the deliberateness comes from acting on a phrase that differs every time, not from making
it tedious.

### 4.4 Archives vs database export

Two different operations, deliberately not shared:

| | `ShopArchive` | `DatabasePathProvider.ExportDatabaseTo` |
|---|---|---|
| Scope | selected shops, row by row | the whole database file |
| Import | **additive** — a `PublicId` already present is skipped | **replaces** |
| Use | download / restore one branch | full backup |

Restoring one deleted shop through the database export would take every other shop with it.

---

## 5. Data and persistence

### 5.1 Schema evolution

The initial EF migration plus **idempotent runtime column guards** at startup
(`EnsureDatabaseCompatibilityAsync`). Guards are a data-driven `OrderColumnMigrations` table iterated
in a loop, not a ladder of `if`s — the ladder had reached ~30 branches and tripped Sonar's cognitive
complexity rule.

This is what lets a feature add a column without a migration ceremony: the column is added on next
launch and reads as null for every existing row.

### 5.2 Shop stamping

`SaveChanges` stamps new orders with the open shop and its currency. `SuppressShopStamping()` exists
for importers: a restored archive carries orders belonging to *other* shops, possibly written on
another machine, and stamping would overwrite all of them with whatever shop happens to be open.

### 5.3 Audit stamps and change detection (v4.1.0)

`LastModifiedDate` / `LastModifiedBy` record who last **edited** an order.

Opening an order and pressing Save without touching anything must not restamp it — that overwrites
the record of who last edited it with the name of whoever last *looked* at it. The check asks **EF**
whether the tracked entity is modified rather than hashing the form: EF holds the loaded values and
compares column by column, so it covers every mapped field including JSON blobs the form does not
model, and keeps covering a column added next year.

Two things this needed:

- The stamp had to move **out** of the apply-the-form method — an unconditional `UtcNow` there makes
  every save look like a change.
- Child rows had to stop being **removed and re-added** every save; they are now compared by value.

**Known and correct:** an order stored before some of today's fields existed comes back with nulls
the form cannot represent, so the editor's defaults are written on its first save and it *is* stamped
— once.

### 5.4 Attached documents

Custom-made record images live under `Documents/CustomMade`, keyed to the record. The database export
packages the whole `Documents/` tree alongside the `.db` and its `-wal`/`-shm` sidecars, so images
migrate with the data. Import is zip-slip guarded and backs up the current state first.

---

## 6. Localization

### 6.1 String tables

One XML file per language under `Settings/System/Languages/<code>.lang.xml`, **discovered** rather
than listed. Adding a language is dropping a file in — proven three times (fr-FR, es-ES, ja-JP).

Five ship today: `zh-CN`, `en-US`, `fr-FR`, `es-ES`, `ja-JP`.

UI text is referred to by **key** (`Order.Fields.FinalBalance`) everywhere in code and comments,
never by its label in any language. Keys are stable and greppable; labels are neither.

### 6.2 Per-shop installed languages

A shop installs a **subset**. `ShopLanguages` is the one answer to "which languages may this session
pick from":

- `Installed(shop)` — never empty; a shop with nothing installed falls back to its preferred code.
- `Selectable(shop, canChooseAnyLanguage)` — every shipped language for an administrator, the
  installed set for everyone else.
- `PreferredCode(shop)` — what a shop **opens** in: its preference when it installs it, otherwise the
  first it does, because the two fields can disagree and a branch must never open in a language its
  own toggle cannot return to.

### 6.3 Currencies derive from languages

`ShopCurrencies` mirrors `ShopLanguages` in shape and differs from it twice, on purpose:

- **No per-user capability.** An administrator sees every language because language is only how a
  screen reads; currency is a fact about the order, so pricing outside the shop's set would be a
  wrong number on a real receipt.
- **Bounded by the `CurrencyType` enum** rather than a discovered folder. Each `*.lang.xml` declares
  its market's currencies under `Currency.Codes`; a declared code the enum cannot name is dropped.

`SymbolOf` reads the **order**, not the shop: what a shop trades in today is a different question
from what an order was priced in.

### 6.4 Source language policy

Source code, comments, XML-doc, log and exception messages, commit messages and all companion
Markdown are **English**, regardless of the language a request was written in. The only sanctioned
non-English text is inside the language files, a verbatim quote of a request in the checkpoint log,
or naming a string-table *value* being changed.

---

## 7. Theme and UI

### 7.1 The theme dictionary

`Themes/AppTheme.xaml` owns the palette, typography and every control template. Colours are named
brushes (`PrimaryBrush`, `SurfaceBrush`, `TextPrimaryBrush`, `DangerBrush`, `BorderBrush`…), never
hex literals at the call site.

### 7.2 Themed controls

Each is defined **keyed and implicit**:

```xml
<Style x:Key="ThemedCheckBox" TargetType="CheckBox"> … </Style>
<Style TargetType="CheckBox" BasedOn="{StaticResource ThemedCheckBox}"/>
```

The keyed form lets a specific control opt in; the implicit form makes it the default.

| Control | Notes |
|---|---|
| `ThemedTextBox` | rounded, focus ring, read-only state |
| `ThemedComboBox` / `ThemedComboBoxItem` | drawn row chrome, hover and selection |
| `ThemedRadioButton` | drawn ring, brand dot, **halo outside the ring** |
| `ThemedCheckBox` | drawn box and tick, indeterminate dash, halo outside the box |
| `ThemedButton` / `PrimaryButton` / `DangerButton` / `OnHeaderButton` | |

Two deliberate details:

- **The hover halo is drawn outside** the ring or box. A fill *inside* on hover reads as
  half-selected.
- **A checked *and disabled* checkbox keeps its fill.** A locked "deposit received" must still read
  as received; the stock grey-out says the opposite of what is true.

### 7.3 The rule that keeps being broken

> **A keyed style with a `TargetType` and no `BasedOn` REPLACES; it never extends.**

This cost three separate debugging sessions — `CustomMadeServiceWindow`'s `TextBox`, `MethodRadio`,
and `OrderEditWindow`'s `FieldLabel`. Each compiled, ran, and looked wrong for weeks.

Before editing a theme style, grep its key across `Views/`. If a window declares its own, the edit
does not reach that window. The fix is `BasedOn="{StaticResource SameKey}"` — legal with the same
key, resolving to the parent dictionary's entry.

### 7.4 Layout traps

- **A horizontal `StackPanel` measures its children at infinite width**, so `TextWrapping` inside one
  is inert and text is clipped rather than wrapped. Icon-plus-prose compositions use a `DockPanel`
  with the icon `DockPanel.Dock="Left"`. This one has been hit twice, the second time one release
  after it was written down.
- Other infinite-width parents: `ScrollViewer` in its scroll direction, `Canvas`, and any `Grid`
  column sized `Auto`.
- **`IsChecked="True"` in markup fires its handler during `InitializeComponent`**, before the
  controls it touches exist. Defaults are set in code behind a `_sectionsReady` guard.
- **A theme trigger with `TargetName` beats a locally-set value.** A control needing to escape that
  owns its own template (`ChallengeBox` in the confirmation dialog).
- **A relative `ResourceDictionary` URI resolves against the *application* assembly**, not the
  assembly whose XAML declares it. The theme is merged by absolute pack URI, and tools that host
  product windows must supply the app icon resource themselves.

### 7.5 Windows

| Window | Purpose |
|---|---|
| `MainWindow` | order list, filters, detail panel, toolbar, receipt |
| `OrderEditWindow` | the order form: customer, three service sections, payments, status |
| `CustomMadeServiceWindow` | one custom-made record: measurements, images, price, PDF export |
| `LoginWindow` | sign-in and pre-shop language choice |
| `ShopPickerWindow` | choose a shop; entry to Store Management |
| `ShopSetupWindow` | shop identity, location/tax, languages, currencies, numbering |
| `StoreManagementWindow` | administrator: delist, delete, download, restore, reinitialize |
| `StoreMembersWindow` | per-shop roster |
| `UserManagementWindow` | installation-wide accounts |
| `SessionActionWindow` | Lock / Sign out chooser (v4.1.0) |
| `LockScreenWindow` | password-only unlock (v4.1.0) |
| `ConfirmDestructiveWindow` | the phrase gate in front of irreversible actions |
| `MeasurementTermsWindow` | the measurement vocabulary: terms, garments, term↔garment maps |
| `MeasurementTermLanguageWindow` | per-language names for a custom term or garment |
| `MeasurementPrintOptionsWindow` | language and unit for a printed measurement sheet |
| `ProductCatalogWindow` | the shop's ready-made product list |
| `ReceiptBrandingWindow` | header/footer rich text, logo and placement, tax number |
| `ShopLocalizationWindow` | the languages a shop installs and the currencies it takes |
| `LanguageSelectionWindow` | pre-shop language choice |
| `DocumentPreviewWindow` | attached image preview |

### 7.6 List rendering

The order list is one line per cell and **every row the same height** — the thing a list read by
scanning down a column cannot afford. Cells never wrap; they trim with an ellipsis and carry a
tooltip. Where a cell needs two lines (the custom-made column: `Yes` over `(Qipao, Shirt)`), the
second line is a **fixed height that stands whether it shows or not**, so rows stay level.

---

## 8. Validation

### 8.1 One rule, one place

`Models/ContactValidation` is the single definition of a usable phone number and email address. The
rules were once private to `OrderEditWindow`; a second copy is free to drift, and an address one
screen accepts and another rejects is a bug nobody sees until mail bounces.

**Blank is valid** in both. "Required" is a separate question the caller answers — the order form
demands an email only when a payment method needs one.

### 8.2 Phone numbers

A phone number carries **the country it belongs to**, chosen per number rather than per shop: a
Toronto shop takes a visiting customer's Shanghai mobile, and validating that against Canada would
refuse a correct number. The shop's location only decides what the picker *opens* on.

`Settings/System/Defaults/phone-countries.json` ships, per country:

| Field | Meaning |
|---|---|
| `dialCode` | shown in front and stored with the number (`+1 905-401-6667`) |
| `nationalDigits` | the digit counts a national number may have |
| `nationalPattern` | the shape its digits take — what actually decides validity |
| `nationalFormat` | grouping per digit count, applied as the number is typed |

**Why a pattern and not a digit count.** Counting digits cannot see a NANP area code starting with 0
or 1, a Chinese mobile not beginning with 1, or a French number carrying a trunk zero — all the right
length, none of them real. Nine such numbers were accepted before patterns were added.

Three things that make the patterns safe:

- **Matched against digits only**, punctuation stripped first, so a pattern describes numbers rather
  than re-stating which separators people type.
- **Anchored at both ends.** An unanchored pattern matches a substring — a validating regex that
  validates nothing. Asserted per country.
- **A positive case per country.** A pattern refusing everything satisfies every negative test.

**Japan deliberately ships no pattern.** It writes `090-1234-5678` (11 digits, domestic trunk zero),
`90-1234-5678` (10, international, no zero) *and* `03-1234-5678` (10, Tokyo, with zero). Any
leading-digit rule refuses a real form. Length is the only rule true of all three — the same call as
its missing 10-digit *format*, for the same reason: the digits do not say which convention is in use.
**A fallback saying "no rule" beats a rule that is wrong.**

#### Strict vs lenient — leniency belongs to the *value*

A number stored before the per-country rule existed must not strand its record: refusing it would
mean an order taken last year could not have its status corrected until somebody re-typed a phone
number they cannot verify.

That argument covers the **stored value** and nothing else. `PhoneNumberField.IsAcceptable` is the
one place the choice is made:

- blank baseline (new record, or one that never carried a number) → **strict**
- the value has been **edited** → **strict**
- a stored value, untouched → **lenient** (shape, 7–15 digits)

Keying this to whether the *record* was new — as it originally was — meant an existing order accepted
any 7-to-15-digit number in any country.

### 8.3 Formatting as you type

Numbers are grouped progressively (`905-401-6667`, `138 0013 8000`, `6 12 34 56 78`) with two rules
that make the field usable rather than merely correct:

- **A separator is emitted only when a digit follows it**, so a half-typed number never carries a
  dash it has not earned.
- **Backspace onto a separator removes the digit in front of it**, because deleting the separator
  alone is undone by the re-group that follows and the key would appear to do nothing.

The caret is restored from `TextChangedEventArgs.Changes` (`Offset + AddedLength`), **not**
`SelectionStart`: whether the box has moved the caret past the new text depends on how the text
arrived — keystroke, paste, `SelectedText`, assignment — and they do not agree.

### 8.4 Every host validates

`PhoneNumberField` is hosted by five windows. Sharing the *control* did not share the *rule*: it
centralised the inputs and the formatting while each window kept its own decision, so there was one
implementation and several omissions.

All five now call `IsAcceptable` and `IsValidEmail`, and the **suite asserts this against the
source** — every window hosting the field must call the shared rule, must validate an email, and must
not name `IsValid`/`IsValidLoose` directly. Driving the five that exist proves today's behaviour;
only the source check constrains the sixth window added later.

### 8.5 Numeric input

| Input | Rule |
|---|---|
| Money | `^\d*(\.\d{0,2})?$`, filtered on keystroke and paste |
| Tax rate | `TaxRateFormat` — up to **3** decimals, 0–100 |
| Measurements | starts with a digit, one `.`, optional trailing `+`/`-` |

A partial-input pattern must accept what a half-typed value looks like (`""`, `"14"`, `"14."`,
`"14.9"`) and must **not** apply the range — `"1"` is the first keystroke of `"14.975"`.

### 8.6 Reporting a refused save

Three surfaces, one code path: a banner at the top (that something is wrong), an inline message under
each field (where), and one dialog listing every problem. `TryValidateForSave` owns the dialog and
delegates the marking to `ValidateForSave` — a `MessageBox` inside a check blocks the thread, so a
harness driving Save would hang on a dialog nothing can answer.

---

## 9. Money, payments and tax

### 9.1 Per-section model

An order carries three independent service sections — **Alterations**, **Custom-made**, **Clothing /
ready-made** — each with its own subtotal, deposit, payment methods, tax rate and cleared flag. Money
is derived, never stored twice: `Order.CalculateSectionPayment(in SectionPaymentInput)` returns a
`SectionPayment` record struct.

`SectionPaymentInput` is a **required struct**, not a widening parameter list. When the pricing-mode
flag shipped as an optional argument, a harness kept the shorter overload and the numbers silently
stopped agreeing. A required struct makes the compiler enumerate the call sites again.

### 9.2 Payment methods and per-portion tax

Each section is settled in two portions — **deposit** and **final balance** — each with its own
method and its own rate. A portion is taxed only when the method that settled it is taxable under the
shop's current rules.

The **rate** comes from the order (what the shop charged and persisted); whether it applies at all
comes from the shop's **current** rules. So a saved order prints the figures it was saved with, while
a method the shop has since made tax free stops adding tax rather than keeping a rate nobody can see.

### 9.3 Split payments (v4.0)

A customer paying a 600 deposit as 400 cash and 200 on a card is recorded as exactly that — and taxed
as exactly that: **26.00**, not 78.00. A single rate per stage cannot express a customer who pays two
ways.

- **Off by default**, per section, per stage. Deposit and final balance decide **independently**
  (`DepositEnabled` / `FinalEnabled`): a customer can hand over the deposit in cash and settle the
  balance across two cards.
- **Only where tax is added at settlement.** Where the price already contains the tax, how a sale is
  tendered cannot move it, so the controls are not shown.
- **A stage that does not add up is refused**, naming the amount. A shortfall would be a partial
  payment, and there is no such state anywhere in the application.
- **Unanswered rows offer the remainder as a placeholder**, re-offered on every keystroke. A row
  holding an amount is an answer; an empty row keeps offering the balance rather than being settled
  at zero on the shop's behalf.
- **"Deposit received" cannot be ticked until the rows balance** — ticking it closes the stage, and
  by then the rows are off screen.
- **A confirmed stage locks its allocation**, exactly as the single-method controls do.

### 9.4 Tax jurisdictions and pricing modes

`Settings/System/Defaults/tax-jurisdictions.json` ships one preset per store location: code,
standard rate, **pricing mode**, default currency.

| Mode | Markets | Behaviour |
|---|---|---|
| Tax **added** at settlement | Canada, US | rate is the shop's to enter; per-method matrix applies; splits offered |
| Tax **included** in the price | China, Japan, EU | the jurisdiction's own rate applies unconditionally; no per-method matrix |

A value-added tax is a property of the **sale**, not of how it was settled — a cash sale in Tokyo
carries the same consumption tax as a card one, so letting a "cash is tax free" rule zero it would
make one price yield two different taxes.

The mode is **frozen onto the order** at save, exactly as the currency is.

Canada is **one entry**, not one per province: the tax-exclusive presets quote no rate at all, and
the shop enters what it collects. The region-widening logic survives, so a shop still stored as
`CA-ON` resolves to its country.

A second pricing mode is a second **vocabulary**, not just a second formula — tax-inclusive markets
show *service total (incl. tax)* with no per-stage rate difference and no deposit-stage breakdown.

### 9.5 Rate precision

Rates carry up to **three decimals** — Quebec's combined GST+QST is **14.975%**.

`TaxRateFormat` is the one definition of the limit, the input pattern, the parser and the display.
Before it, the rate was `decimal` end to end and stored correctly, but every *display* used `"0.##"`
— and the settings screen seeds its edit box from that formatted string, so opening the tax settings
and pressing Save rewrote 14.975 as 14.98. **A format that an edit box is seeded from is part of the
data path, not decoration.**

### 9.6 Money rounding

`MoneyRounding.Round` — two decimals, **half away from zero**. 89.425 is 89.43.

- `decimal.Round` defaults to banker's rounding, which gives 89.42 — a till arguing with a figure the
  customer worked out on paper.
- **The parts are rounded, then added.** Rounding only the total lets a section print three lines
  that visibly do not sum to the figure beneath them. Every figure a customer can see is one they can
  add up. Costs at most a cent against the unrounded ideal.

This was invisible for a long time because `ToString("N2")` rounds on the way to the screen: every
figure *looked* right while the values behind them carried full precision. A third decimal on the
rate is what made it an everyday problem rather than a rare one.

### 9.7 Balance status

`PaymentStatusKind` (`Outstanding` / `ClearedPickedUp` / `ClearedNotPickedUp` / `Refunded`) is the
single source of truth for the status indicator — label and colour — across the list, the detail
panel and the receipt.

---

## 10. Printing and documents

### 10.1 Receipts

`FlowDocument` + `PrintDialog`. Per-service subtotals, per-item unit prices and line totals, the
payment breakdown, and the balance status.

### 10.2 Measurement sheets

QuestPDF, laid out by `MeasurementSheetDocument`, which takes **plain, already-localized data with no
string keys**. The sheet is produced in the language chosen in the print dialog rather than the UI
language, so the composer must not look anything up.

It lives outside the window that gathers the data because a window cannot be opened without a message
loop, and a print layout checkable only by a human clicking Export is one whose regressions ship.

### 10.3 Branding

`ReceiptBrandingStore` holds per-language header/footer XAML, a logo with a placement, and the tax
registration number. `BrandingRenderer` round-trips content between a `RichTextBox` FlowDocument and
its XAML string, appends it to a printed receipt, and renders the same XAML into QuestPDF spans.

`ShopLetterhead` is what the application **generates** when no custom header is supplied — name,
subtitle, contact lines, tax line. A custom header **replaces** it rather than stacking on it. Both
the receipt and the measurements sheet consume it, because they had drifted: the measurement paths
once printed a bare tax line above the title while never naming the shop.

---

## 11. Configuration and file layout

### 11.1 Two trees, two lifetimes

| | Shipped configuration | Per-installation state |
|---|---|---|
| Where | `Settings/System/**` (in git) | `%LOCALAPPDATA%\CameywareOrder` |
| Lifetime | replaced wholesale by an upgrade | must survive one |
| Contents | language files, tax jurisdictions, phone countries, app defaults | database, credentials, settings, branding, documents |

### 11.2 Locating shipped files

`SystemSettingsPaths` probes `AppContext.BaseDirectory`, then the working directory — and asks
whether **the file** is there, not whether a folder is. A deployment carrying the folder with only
some of its files would otherwise win the probe, and every missing file then reads as absent while
each loader degrades **silently** to its built-in fallback.

### 11.3 Shipped data files

| File | Contents |
|---|---|
| `app-defaults.json` | seed defaults |
| `tax-jurisdictions.json` | tax presets per location |
| `phone-countries.json` | dial codes, lengths, patterns, grouping |
| `Languages/<code>.lang.xml` | one string table per language |

Every one is read defensively with a hard-coded fallback, so a missing or corrupt file can never
leave a form unable to accept input. A single unparsable entry costs that entry, not the file.

---

## 12. Quality gates and verification

### 12.1 The gates

| Gate | Standard |
|---|---|
| Build | 0 warnings, 0 errors |
| Sonar | 0 issues, every severity and category |
| Harness suite | 26 harnesses, ~1,700 assertions, all green |
| CJK sweep | no non-English text outside the sanctioned places |

Sonar runs **inside `dotnet build`** via `Directory.Build.props`, not as something to remember to
check. The first run as an analyzer found nine issues across six files in a workspace that had been
called clean for months.

### 12.2 The harness suite

Console applications in a scratchpad, each driving the real assemblies — real windows, real database
copies, real string tables. They are run from the project root, because `SystemSettingsPaths` probes
the working directory and running them elsewhere makes every shipped preset read as absent while each
loader silently falls back.

A harness that reads live user data has to **establish** the state it asserts on. Several have gone
red months later over drifted real data and read like regressions when nothing had broken.

### 12.3 Verification discipline

- **Render anything visual.** A template, a layout and a formatted value are what no assertion can
  judge. Rendering has caught clipped labels, a mis-punctuated phone number and a checkbox whose
  disabled state read as the opposite of the truth — all of which compiled, ran and passed green.
- **Prove a new assertion can fail** before trusting it. Watch for a fixture sitting on a *fallback*
  path, where two readings produce the same value and the test cannot tell the branches apart.
- **Check a red harness on a clean checkout** before diagnosing it as yours.
- **For "every X must do Y", assert against the source.** Behaviour tests cover what exists;
  only a source check constrains what gets added next.

---

## 12a. Release history

Version numbers track the shape of the change: patch for a fix, minor for a feature.

| Version | Change |
|---|---|
| v3.0.0 | store location decides the tax, and whether prices already contain it |
| v3.2.0 | Store Management — delist, delete, download, restore, reinitialize |
| v4.0.0 | split a payment stage across payment types, taxed per tender |
| v4.0.1 | split-payment hotfix: skip deposit, balance breakdown, placeholders, locks |
| v4.0.2 | drawn radio buttons and checkboxes; two labels that would not wrap |
| v4.0.3 | phone numbers punctuate themselves; reason section wraps; list column stacks |
| v4.0.4 | three-decimal tax rates; money rounds half up; Sonar to zero and into the build |
| v4.1.0 | lock the session; stop restamping unchanged orders |
| v4.1.1 | per-country phone patterns; a retyped number is held to the rule |
| v4.1.2 | the custom-made record checks its contact details |
| v4.1.3 | every phone and email field validated, through one rule |

---

## 13. Cross-cutting principles

These recur across every area above.

1. **One definition, shared.** `ContactValidation`, `TaxRateFormat`, `MoneyRounding`, `PersonName`,
   `ShopLanguages`, `ShopCurrencies`, `PaymentTaxRules`. A second copy of a rule is free to drift.
2. **Sharing a component is not sharing a rule.** Ask what each screen *decides*, not just what it
   displays.
3. **Data over code for anything a shop varies.** Tax presets, phone rules, languages, catalogues and
   measurement terms are shipped JSON/XML, read defensively, editable without a rebuild.
4. **Make the compiler enumerate call sites.** Required struct parameters over optional arguments —
   silence at a call site is how arithmetic drifts.
5. **Never rewrite stored data on read.** A number already in the database is a fact about a
   customer, not something to reformat on load.
6. **Grandfather the value, not the record.** Leniency for legacy data is keyed to whether the value
   was touched.
7. **Degrade to "no rule" rather than to a wrong one.** Japan's phone pattern, Japan's 10-digit
   format, the tax-exclusive presets that quote no rate.
8. **A silent fallback is a defect waiting to be misread.** Every loader degrading quietly to a
   built-in default is why a missing file surfaced as unrelated assertions failing.
9. **State the reasoning where the decision lives.** The comment explains *why this and not the
   obvious alternative*, because the alternative is what the next reader will reach for.
