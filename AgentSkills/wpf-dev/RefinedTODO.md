# RefinedTODO — CameywareOrder

**This is the file to read.** `TODO.md` remains the full development record and is
still written on every task, but it is 83 entries and ~220 KB — reading it to plan
work costs more context than the work itself.

Maintained per `SKILL.md` §0 Step D. In short: every task appends to `TODO.md` in
full **and** lands here condensed, after which nearby entries are merged and
superseded instructions are deleted rather than annotated. Durable engineering
lessons move to `context.md`; this file keeps *what was done and where it stands*.

Condensed 2026-07-28 from 83 entries.

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
- **Quality gates** — build 0 warnings / 0 errors, Sonar zero findings, and a
  scratchpad harness suite (~341 assertions across 9 harnesses) that must stay green.

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
