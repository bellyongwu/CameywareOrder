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
  email, website, tax registration number, currency, payment/tax rules, receipt
  numbering, measurement terms, branding and product catalogue.
- **Auth** — per-shop memberships with roles, activation, shift; `admin` is
  permanent and never named on screen.
- **Languages** — zh-CN, en-US, fr-FR. One file per language, discovered from
  `Settings/System/Languages`; adding a language is dropping a file in.
- **Configuration** — shipped config in `Settings/System/**` (read-only, in git);
  per-installation state under `%LOCALAPPDATA%\CameywareOrder` via `UserDataPaths`.
- **Quality gates** — build 0 warnings / 0 errors, Sonar zero findings, and the
  scratchpad harness suite, which must stay green. A harness that reads live user
  data has to **establish** the state it asserts on: several have gone red months
  later over drifted real data and read like regressions when nothing had broken.

## Open

Nothing in flight. The last multi-phase effort (systematic config refactor, phases
0–3) is complete.

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

## Recent work (2026-07-27 → 07-28)

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

Editable in two places on purpose. 店铺成员 reaches people who belong to a shop;
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
measurements could not be exported in — while the PRINT dialog beside it was
already dynamic. Now built from `LocalizationService.AvailableLanguages`, like the
print dialog and the login screen.

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
`MeasurementTermsService`; managed at 本地配置 → 商品类别, seeded from shipped
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
order settled as greeting · 本地配置 · 语言 · 店铺成员 · 退出; right-click menu themed;
theme, typography and panel transitions modularised.

Measurement-term gender picker: three radios → a drop-down. Radios need the width
of **every** label at once — measured at ~291 px in Chinese (fits a 420 px dialog),
~429 px in English and ~463 px in French (both overflow). Symbols reuse the ♂/♀
characters the terms list already badges with, via the shared
`MeasurementGenderPresentation`.

> A right-anchored 本地配置 menu was built and then reverted — the caret and content
> flipped sides and it read worse. 本地配置 and 店铺成员 were swapped instead. Do not
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
global · 本地配置 menu · per-portion payment tax split · app icon and welcome header.

**2026-07-23** — alteration category dropdown · cm/inch toggle and localized
measurement download · order locking and status filter · detail-panel pricing ·
first workspace-wide Sonar cleanup.
