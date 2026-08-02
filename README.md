# Cameyware Order

A multi-shop, multi-user desktop order-management application for bespoke tailoring. It records
customer orders across three service lines, tracks the money owed and received on each one
independently, captures made-to-measure body measurements, and prints receipts and measurement
sheets.

Windows desktop app: **WPF on .NET 8**, with all data stored locally.

---

## Latest release

### v8.1.0 — 2026-08-02

Follow-ups to the data release, and one new piece the rest of the application will build on.

- **Advanced search is folded away.** Finding a customer is typing a name into the search box, so the
  *Look in*, *From* and *To* controls now sit behind an **Advanced search** button and the bar opens
  as one clean row. If you close it while a date range or a narrowed field is still in force, the
  button shows a dot — a list quietly filtered by something you cannot see is the one thing this must
  never do.
- **Clear filters looks like a button again.** It was styled as one more input in a row of inputs;
  it now carries the same tint as the other action buttons.
- **A progress bar for anything that touches data.** Refreshing, copying and deleting now dim the list
  and show what is happening. It is built as one shared piece rather than as part of the order screen,
  so every screen added later gets the same indicator by asking for it.
- **Export all orders replaces Refresh** in the records bar, and now writes **every** order the shop
  holds rather than the filtered list. **Press F5 to refresh** — which is what most people were
  already trying.
- **Backup & Recovery moved into Local Configuration → Local Database**, at the top of it. Both
  entries answer for the same thing — the file your data lives in — and having one beside the other
  made "where do I go" a question with two answers.

### v8.0.0 — 2026-08-02

**The data release.** Three things a shop running this on one computer needs and did not have: a
backup that happens by itself, a way to find and export orders, and a way back from a mistaken
delete.

- **The application backs itself up.** A safety copy used to be taken only when you imported a
  database — so a shop that never used Import/Export had none at all. It now copies itself on a
  schedule (once a day out of the box), keeping the last ten copies. The copy runs **when the
  application starts, never while somebody is working**, and holds the whole database and every
  attached image. **Local Configuration → Backup & Recovery** shows when the last one ran, lets you
  take one on the spot, and has an *Open the folder* button — a backup that never leaves the computer
  does not survive the computer, so put one on a USB stick now and then.
- **Restore from any copy.** Pick one, type the confirmation phrase, and every shop's data is
  replaced by it. What is being replaced is itself copied first, so restoring the wrong file can be
  undone.
- **Deleting an order is no longer permanent.** It goes to a **Recycle Bin** (Local Configuration →
  Recycle Bin) and can be put back exactly as it was, receipt number included, for **30 days** —
  adjustable, or set to keep them until you empty the bin yourself. After that it is removed when the
  application next starts. Emptying the bin, and *Delete for good*, are the only places in the
  application where a record cannot be recovered afterwards, and both ask you to type a phrase first.
- **Search by order number.** The search box matched the customer's name and phone number only, which
  is not what a customer standing at the counter is holding. It now looks in the receipt number, the
  name, the phone, the email, the address and the notes — with a *Look in* box to narrow it to one of
  those — and a **From / To** date range beside it.
- **Export the orders as a spreadsheet.** *Export CSV* writes **exactly what the list is showing**,
  filters and all: one row per order, per-service subtotals and tax, what was received and what is
  still owed. It opens correctly in Excel in every language the application ships. The figures come
  from the same place the receipt does, so the sheet and the paper cannot disagree.

**Three new permissions** — *Recycle bin*, *Export orders* and *Backup and recovery*. On a **new**
installation the manager gets the first two and the administrator gets all three. On an **existing**
installation nothing is granted automatically: your roles keep exactly the permissions you gave them,
so an administrator should hand out the new ones in Local Configuration → Permissions. That is
deliberate — an upgrade should not quietly widen what anybody is allowed to do.

### v7.1.0 — 2026-08-02

**Everything that changes which shops exist now lives in one screen.** Select Shop is a chooser
again; Store Management is where a shop is created, copied, taken out of service or removed.

- **Create New Shop and Create demo store have moved into Store Management**, into a new *Add a
  store* card at the top of its action column. Select Shop no longer creates anything — two screens
  that could both make a shop is how a branch list ends up with a duplicate nobody meant to create.
  A shop you create there is selected for you when you come back, so it is still one click to Open.
- **The demo store now arrives with a hundred orders.** It used to be an empty shop with sensible
  settings, which showed you the screens and none of the work: no takings, no pickup queue, nothing
  in the settlement report. The hundred orders cover four months of trade — alterations, ready-made
  and made-to-measure, some collected, some still open, a few refunded — and **their dates are
  worked out from the day you create it**, so the demo store always looks like a shop that is
  trading today rather than one that closed whenever this version was built.
- **One demo store at a time.** The button disappears once you have one and comes back the moment
  you delete it, so the demo history can never quietly turn up twice in your figures. Deleting the
  demo store removes its hundred orders with it, exactly as deleting any shop does.
- **Copy a shop.** Select one or several in Store Management and press Copy. Each copy takes the
  original's settings, currencies, languages, tax rules, product categories, measurement terms and
  receipt header — and **none of its orders**, which belong to the branch that took them. The copy is
  named after the original with *(copy)* after it, and *(copy 1)*, *(copy 2)* and so on when that
  name is already in use. It starts its own receipt numbering at 1.
- **Ctrl+C and Ctrl+V copy records.** In Store Management, copy the selected shops and paste to
  duplicate them; in the order list, copy the selected orders and paste to duplicate those. The same
  keys, doing the same thing, on both screens — and on any list that gains the ability later, because
  the shortcut is one shared piece of the application rather than something each screen spells out
  for itself. Copying in one shop and pasting in another is refused: an order belongs to the branch
  that took it.

### v7.0.1 — 2026-08-01 (hotfix)

Six things reported after v7.0.0 shipped.

- **The month's summary on the main screen now obeys the permission.** It was hidden correctly when
  the shop opened and then came back on the next refresh — a role without "View the settlement
  report" could still read the month's takings from the home screen.
- **Pick a period directly in the settlement report.** Previous and Next stepped one period at a
  time, which is fine for last month and useless for last March. The period is now a button: click it
  and choose the day, the month or the year, in a calendar that matches whichever you asked for.
- **The report closes with ESC**, like every other panel. With the period calendar open, ESC closes
  that first.
- **A Minimize button on the sign-in and lock screens.** Both offered only Close.
- **Permissions and User Management have swapped places** on the shop-picker screen — what a role may
  do is decided before anybody is put into it — and each administrator button now carries its own
  colour, so the row can be aimed at without reading all four.
- **The month and year cells of every calendar** are now sized and coloured like the day cells. They
  had been left with the operating system's own styling, which only showed once a calendar could be
  opened straight into the months view.

### v7.0.0 — 2026-08-01

**Permissions are yours to set.** Until now the application shipped three fixed roles and decided in
code what each of them could reach. A shop that wanted somebody who reads the settlement report and
touches nothing else had no way to say so. Now every feature the application gates is a permission
you can hand out, and a role is simply a named set of them.

- **Local Configuration → Permissions**, and the same button on the shop-picker screen. Administrators
  only.
- **Two columns, two questions.** On the left, your people: open one to see the shops they work in and
  tick the roles they hold in each. On the right, your shops: open one to see the roles, and open a
  role to see — and change — exactly what it may do.
- **19 permissions**, grouped as Orders, Reporting, Shop configuration, People and access, and
  Installation. Creating an order, deleting one, printing a receipt, reading the report, exporting it,
  editing the measurement terms, and so on: each is now its own tick box.
- **Define your own roles.** Add one, name it, choose what it may do. **Auditor** is already there as
  an example — it reads orders and the settlement report and cannot change anything at all. Delete it
  if you do not want it; the deletion sticks.
- **Manager and Staff can be adjusted** to suit how your shop actually runs, and put back to how they
  shipped with one button. They cannot be deleted, because people are assigned to them.
- **Nothing changed for anybody on upgrade.** Manager and Staff start with exactly the access they had
  before this release, including Staff still seeing the settlement report. Trim it if you want to —
  that is what the panel is for.
- **The screens follow the permissions.** A role without "Edit orders" opens an order read-only and
  says why; without "Create orders" the New Order button is not there; without "View orders" the list
  does not load at all. The three permissions that govern accounts and permissions themselves are the
  administrator's alone and cannot be given away.

### v6.0.1 — 2026-08-01 (hotfix)

Two things on the settlement report, both reported after v6.0.0 shipped:

- **The small cards under Orders and Payments now sit on an even grid.** They had space between
  them across but none between the rows once six of them wrapped onto a second line, so the two rows
  ran together.
- **Date boxes size themselves to the space they are in.** The report's custom From / To boxes were
  pinned at a fixed width, which left the calendar that drops out of them nearly three times wider
  than the box itself. Every date field in the application now stretches with its column and simply
  refuses to go below a sensible minimum — so the box and its calendar line up.

### v6.0.0 — 2026-08-01

**Settlement reporting.** The shop can now close off a period and see exactly what it took.

- **Local Configuration → Settlement Report.** It opens on **this month** — the 1st to the last —
  which is the period most shops settle on. Day, Year and a custom range are one click away, and
  Previous / Next step a whole period at a time.
- **Every figure, before and after tax.** Total charged before tax, the tax on it, the total with
  tax, what has actually been received, and what is still owed.
- **Split by what earned it** — alterations, made-to-measure, and ready-made stock — each with its
  own before-tax, tax, after-tax, received and outstanding figures, and a total line under them.
- **Split by how it was paid** — cash, card (debit, credit and older undifferentiated card records
  together), and bank transfer. The methods always add up to the money received; that is asserted,
  not hoped for.
- **Order counts**: how many were taken, how many are **not finished**, how many were completed,
  shipped, **cancelled** and **returned**. Cancelled and returned orders are counted but earn
  nothing — their value is reported on its own line rather than quietly folded into the takings.
- **A doughnut chart and a bar chart** show the same figures at a glance: revenue by service, money
  received by method.
- **Print or save as PDF**, on your shop's own letterhead, with your header, footer and logo — the
  same branding the receipt and the measurement sheet use. The charts go into the PDF exactly as
  they appear on screen.
- **The report can be read in another language** without switching the application, the same
  Preview-in picker as Measurement Terms. The PDF comes out in whichever language you are reading.

**A summary strip on the main screen.** Above the order list: this month's after-tax total, what has
been received, what is outstanding, the tax collected, and how many orders are unfinished. Click it
to open the full report. It reads the same figures the report does, so the two cannot disagree.

### v5.1.0 — 2026-08-01

**Every order now records the day the customer is coming back for it, and the list is built around
that day.**

- **A new Expected Pickup field** sits beside the order date, on the same row. It starts **empty** and
  it is **required** — you cannot save an order without agreeing a date, and a form saved without one
  says so under the field, in the banner and in the dialog.
- **It has to be a day still to come.** Today and everything before it are struck through in the
  calendar.
- **The list is ordered by it**, soonest first, instead of by when a record was last touched. The
  order that needs doing next is the one at the top.
- **Rows change colour as the day approaches.** Amber inside two weeks, red once the day has passed.
  Nothing to read, nothing to filter — the list tells you at a glance which jobs are running out of
  time.
- **A finished order is never coloured, and never sits at the top.** Something collected last month
  cannot be late, and it does not belong above this week's work.
- **The date is shown as its own column** and in the details panel, so what the colour and the
  ordering are based on is always on screen.

**Orders taken before this release have no pickup date**, and none has been invented for them. They
sort below the ones that do have a date and are never coloured. Give one a date by opening it and
saving.

### v5.0.1 — 2026-08-01 (hotfix)

**The application now knows its own version number.** Until now the release number lived only in this
file, so a built copy could not tell you what it was — right-clicking `CameywareOrder.exe` and asking
for its details showed `1.0.0` for every release ever shipped. The number is now stamped into the
build, and it is the one shown here.

Nothing else changed: no screen, no setting, no data.

### v5.0.0 — 2026-08-01

**Check a translation without changing the language you work in.** Measurement Terms now has a
**Preview in** picker in its top-right corner. Choose a language and the whole panel — the headings,
the buttons, and every garment and measurement name — is re-read in it, right there. The application
itself does not move: the rest of the app, and the language you get back to when you close the panel,
are exactly as you left them.

- **It offers the languages your shop installs**, the same set as the toolbar's language switch, and
  it disappears entirely for a shop that runs in one language.
- **The picker itself stays in your own language.** Preview Japanese and the words *Preview in* stay
  readable, so you can always find your way back.
- **Confirmations stay in your own language too.** "Delete this measurement?" is a question you have
  to answer, so it is never asked in the language you are only previewing.
- **Renaming while previewing edits THAT language's name.** Look at the panel in Japanese, rename a
  measurement you added yourself, and you have filled in its Japanese name — which is the quickest
  way to complete a translation.

**Why this is 5.0.0 rather than 4.4.0.** The preview is built on a piece anything can now use: a
panel can be given its own language in three lines, and the picker is a control you drop onto it.
Other screens can gain the same button without inventing it again.

**The source tree has been reorganised.** `Views`, `Models` and `Services` each now hold the same
three folders — **UserManagement**, **StoreManagement** and **Global** — so where a file lives
answers who it belongs to. Nothing about the running application changed: no window moved, no
setting moved, and your data is untouched. It is a change for whoever reads the code next.

### v4.3.0 — 2026-08-01

**You can now say when an order was actually taken.** Until now every order was stamped with the day
you typed it in. If a customer came in on Monday and the order only got entered on Wednesday, the
record said Wednesday — and there was nothing you could do about it.

- **A new Order Date field** sits in the order form, under Status. It opens on today, so an order
  entered as it is taken needs nothing from you: leave it alone and it behaves exactly as it always
  did.
- **To record an order from an earlier day**, pick that day. The order is filed under it — in the
  list, on the detail panel and on the receipt.
- **You cannot pick a day that has not happened yet.** Future days are struck through in the
  calendar, and typing one in is refused when you save.
- **You can also correct the date on an order you already saved.** Reopen it, change the date, save.
  Saving without touching the date leaves it exactly as it was and does not mark the order as
  edited.
- **The date drop-down is twice as wide** and easier to read and click. Every date field in the
  application uses the same one, so the birthday and start-date pickers under Store Members got the
  same treatment.

**One fix that came with it.** Order dates were stored in universal time but shown raw, so a shop in
a timezone ahead of London could see an order taken this morning listed under yesterday's date. The
list, the detail panel and the receipt now all show the date in your own local time, and they agree
with each other. The receipt no longer prints a time of day beside the order date — the record is a
date.

### v4.2.0 — 2026-07-31

**Pick several records and act on them at once.** Until now the order list let you work on one record
at a time, so clearing out ten test orders meant ten rounds of select, delete, confirm.

- **Ctrl + click** adds a record to the selection, or takes it back out. A plain click still selects
  one record, exactly as before, and Shift + click takes a run of them.
- **Ctrl + A** selects every record on the page you are looking at. It is the page, not the whole
  database — what you can see is what you have picked.
- **Copy and Delete work on the whole selection.** Delete asks once and names how many records it is
  about to remove; Copy makes one new order per record selected.
- **A counter appears beside the record count** as soon as more than one is selected, so how far
  Delete reaches is on screen before you press it — rows you picked can be scrolled out of sight.
- **Everything else still needs exactly one record.** Opening an order, and the three print actions,
  grey out while a group is selected: there is no single order for them to act on. Right-clicking
  inside your selection keeps it; right-clicking a record outside it switches to that one, so the
  menu never quietly reaches a record you only pointed at.

**Copying now uses your shop's own receipt numbering.** This is a fix that came out of the above.
Copy had always stamped its own `ORD-` number built from the date and time, ignoring the prefix and
numbering style set in Shop Settings — so a shop numbering orders `INV-0001, INV-0002…` got a
date-stamped number from Copy and nothing else. Worse for the new feature: because the number was
built from the clock to the second, copying several records at once gave **every copy the same
number**. Copies now draw the next free number from the shop's own run, one each.

### v4.1.3 — 2026-07-31 (hotfix)

**Every screen that collects a phone number or an email now checks it.** The rule had been written
once per screen, and three of the five had it wrong or missing:

- **Shop setup validated nothing.** A shop could store a phone number no country issues and an email
  that was not an address — and then print both on every receipt it hands out, which is the one place
  a wrong number gets copied by the people who need it.
- **Store members and user management always used the lenient rule**, so a number typed into either
  was never checked against its country. As with orders, that leniency is meant for a number already
  stored — one nobody can re-verify — and not for one being typed now.

All five screens now ask the same question of the same shared rule, and the suite checks the *source*
of every one of them: a new window that hosts a phone field and forgets to validate it, or reaches
past the shared rule to pick its own, fails the build's checks rather than shipping.

Nothing gets stricter for data already saved: a stored number left untouched is still accepted
everywhere, and both fields stay optional.

### v4.1.2 — 2026-07-31 (hotfix)

**The custom-made record checks its contact details, as the order form does.** That window collects a
phone number and an email and validated neither — it only checked the phone box was not empty, and
wrote whatever was in it, and whatever was in the email box, straight into the record. Every rule the
order form applies could be walked around by editing the record instead.

Both now apply the same rules: the number must be a real one in the country picked for it, and an
email, if given, must be a well-formed address. Blank stays allowed for the email, as on an order.

The rule itself now lives in one place — on the phone field the two windows share — rather than being
written out once per screen, so they cannot drift apart again.

### v4.1.1 — 2026-07-31 (hotfix)

**Phone numbers are checked against the country they belong to, whichever order they are on.**

- **A number typed into an existing order is now held to the same rule as a new one.** The lenient
  rule exists for numbers taken before the per-country check existed, so that an old order can still
  have its status corrected without somebody re-typing a phone number they cannot verify. It was
  being applied to the whole field rather than to the stored value, so an existing order accepted any
  7-to-15-digit number in any country. It now applies only while the stored number is untouched;
  anything typed is checked properly, and restoring the original makes the record saveable again.
- **Each country now has its own pattern, not just a digit count.** Counting digits cannot see an
  area code starting with 0 or 1, a Chinese mobile that does not begin with 1, or a French number
  carrying a trunk zero it should not — all of these have exactly the right number of digits, and all
  of them used to be accepted.
- **Japan is deliberately checked by length alone.** It writes numbers both with the trunk zero
  (`090-1234-5678`) and without it (`90-1234-5678`), and nothing in the digits says which — so a
  pattern would refuse one form or the other.
- **The message says which problem it is.** A number of the wrong length is told the length; one that
  is the right length but not a real number is told that instead, rather than being told it needs the
  number of digits it already has.

### v4.1.0 — 2026-07-30

**Lock the machine instead of signing out.** Stepping away no longer means losing your place.

- **Press ESC on the main window** and a panel asks what you meant: lock, or sign out. The Lock button
  in the toolbar, beside Sign Out, opens the same panel.
- **Locking hides everything and asks only for your password.** The store you were in reopens
  straight away — no picker, no choosing it again — which is the whole difference from signing out.
- **A lock cannot be dismissed.** There is no Cancel: closing the window, or Alt+F4, ends the session
  and returns to sign-in rather than letting anyone past. Only the account that locked the session
  can unlock it; somebody else uses *Sign out instead* and starts a session of their own.
- **Your access is re-checked on the way back in.** If your membership of that store was withdrawn
  while the machine sat locked, unlocking returns you to sign-in rather than into a store you may no
  longer enter.
- Signing out is unchanged, and the same rule applies to both: close any open order window first.

**A save that changes nothing is not a change.** Opening an order, reading it and pressing Save used
to restamp it — overwriting the record of who last *edited* the order with the name of whoever last
*looked* at it, and moving the date with it. The order is now compared against what is stored, column
by column, and left alone when nothing moved.

One thing worth knowing: an order saved before some of today's fields existed can come back with no
recorded deposit or payment method, and the form fills those in when it opens. Saving such an order
really does change it, so it is stamped — once. Every save after that is compared like any other.

### v4.0.4 — 2026-07-30 (hotfix)

- **A tax rate can carry three decimals.** Quebec's combined GST+QST is **14.975%**, and the settings
  screen could not hold it: the rate was stored correctly but shown rounded to 14.98, and because the
  edit box was filled from what was shown, opening the tax settings and pressing Save wrote the rounded
  figure back. On a $600 sale that is 6 cents of tax, every time, in the shop's favour and against its
  filing. Rates now read and save in full wherever they appear — the settings matrix, the order's tax
  line, the per-payment split rows and the location's own quoted rate.
- **The rate box only accepts a rate.** It previously accepted any text at all, and a fourth decimal
  was taken and quietly rounded on the next load. It is now refused as it is typed, and the box is wide
  enough to show 14.975 without scrolling.
- **Calculated amounts round to the cent, and a half rounds up.** 89.425 is 89.43. Nothing was rounding
  at all before: the screen looked right because the display format rounds, while the figures behind it
  kept their full precision — so a total could differ from the lines above it. Where a payment is split,
  **each line rounds before they are added**, because each line's tax is printed beside its amount and
  a shop has to be able to check the total by hand.

### v4.0.3 — 2026-07-30 (hotfix)

- **A phone number punctuates itself as it is typed**, the way the country it belongs to writes one:
  `905-401-6667` in Canada and the US, `138 0013 8000` in China, `6 12 34 56 78` in France,
  `612 345 678` in Spain, `090-1234-5678` for a Japanese mobile. The grouping follows the country
  picked for **that number**, so a Toronto shop taking a Shanghai mobile sees it grouped the Chinese
  way. Where a country has no single correct grouping the number is left exactly as typed — Japan's
  ten-digit landlines are written both `03-1234-5678` and `045-123-4567`, and nothing in the digits
  says which. A number already saved is re-punctuated only when that is unambiguous: one carrying an
  extension or a note is shown exactly as it was stored.
- **The Cancellation / Return Reason section wraps** instead of running off the end of its column.
  The label, the reason list and the placeholder all wrap; the label was being cut mid-word.
- **The custom-made column in the order list reads over two lines** — the *Yes* on one, the garments
  underneath. Every row stays exactly the height it was.

### v4.0.2 — 2026-07-30 (hotfix)

Appearance and legibility, across every window.

- **Radio buttons and checkboxes are drawn by the application** rather than by Windows, so a form no
  longer mixes the product's own inputs with the operating system's. Both answer the pointer with a
  halo around the control instead of a fill inside it — a fill on hover reads as half-selected — and
  both carry a real disabled state. A checkbox that is **ticked and locked keeps its tick and its
  colour**: a deposit confirmed earlier in the order must still read as confirmed, and the ordinary
  grey-out says the opposite of what is true.
- **The Custom-Made record window is on the shared theme.** Its inputs had kept the default Windows
  look — square corners, a different border, a different focus ring — because the window declared a
  text-box style of its own that replaced the theme's rather than extending it.
- **The price-breakdown labels fit, and wrap when they cannot.** "Pre-Tax Service Total" and its
  neighbours ran past the column and were clipped mid-word.
- **The Basic Information labels wrap, and sit centred against their fields.** A label long enough to
  need two lines — as several are in French and Spanish — was cut off instead.

### v4.0.1 — 2026-07-30 (hotfix)

Fixes and sharpening for the payment split, all in the order editor.

- **Skipping the deposit now means a deposit of zero, rows included.** The split rows kept whatever had
  been typed before "Skip deposit" was chosen, so the section owed a deposit it had just been told not
  to take — and the allocation could not balance against a target of nothing.
- **The balance stage shows its breakdown again**, including when the deposit was skipped. It had been
  keyed to the deposit-received tick, which a skipped deposit never sets, so those orders reached the
  final stage with nothing explaining the figure.
- **The split toggle is offered at the balance stage too**, under that section — and the two stages
  decide **independently**. A customer can hand over the deposit in cash and settle the balance across
  two cards; tying both to one answer meant that choosing to split a balance re-shaped how a deposit
  that had already been taken was recorded. Both toggles open on "no split", as every section does.
- **Every unanswered row offers what is still unallocated**, as a placeholder rather than a value, and
  re-offers it on every keystroke. With 600 to allocate, all four rows open showing 600; put 200 in cash
  and the other three show 400; take 300 of that on a card and the two still empty both show 100.
  Clicking into an empty row fills it with the remainder as ordinary editable text. Rows are otherwise
  never written to: one that already holds an amount is an answer, and one still empty keeps offering
  the balance rather than being settled at zero on the shop's behalf.
- **"Deposit received" cannot be ticked until the deposit's rows add up to it.** Ticking it closes that
  stage and opens the next, so it must not be possible over an allocation that does not balance — by
  then the rows are off screen.
- **Allocating more than the stage owes says so in those terms** — *you have allocated 600 too much* —
  rather than describing an over-payment as something left unallocated, which is what one message
  serving both directions used to do. The line above it already states the allocation against the
  target, so this one names what is wrong. The check runs as the amount is typed, not at save, as does
  the balance that gates "deposit received".
- **Leaving an over-allocated row pulls it back to the most it can hold.** The row corrected is the one
  just edited — the others are amounts already agreed, and moving those to make room would silently
  rewrite a payment that was recorded correctly. It happens on leaving the field rather than on each
  keystroke, which would rewrite "900" at the first digit and never let a second one be typed.
- **Each row states its own tax and what is receivable for it** — `13% tax $39.00 · due $339.00` — so
  the amount actually asked for at the till is on screen per payment type, not only as a section total.
- **A confirmed stage locks its allocation.** The split rows and their toggle now follow exactly the
  locks the single-method controls already had: the deposit's freeze when the deposit is marked
  received, the balance's when the section is settled, and both when the order is read-only. They had
  been left editable, so a stage whose money was confirmed could still be re-apportioned — with the
  deposit rows off screen by then, nothing on the card would have shown it.

### v4.0.0 — 2026-07-30

**One stage, several payment types.** A customer paying a 600 deposit as 400 in cash and 200 on a card
is now recorded as exactly that — and taxed as exactly that.

- **Off by default.** Every payment card carries a *No split payments* / *Split payments* choice, and
  the first is what it opens on. A shop that never touches it sees the form it has always seen, and
  every order already in the database keeps the arithmetic it was saved with.
- **The tax follows each payment, not the stage.** 400 cash and 200 on a card at 13% is **26.00** of
  tax, not 78.00. This is the whole point of the release: a single rate per stage cannot express a
  customer who pays two ways, and the old model had to pick one method for the whole portion.
- **Any combination, at either stage.** Two types, three, or all four; the deposit and the final
  balance are split independently, each against its own target.
- **It applies only where tax is ADDED at settlement** — Canada and the US. Where the price already
  contains the tax (China, Japan, the EU) the controls are not shown at all, because how a sale is
  tendered cannot move a tax that is already inside the price.
- **A stage that does not add up is refused**, naming the amount left over. A shortfall would be a
  partial payment, and there is no such state anywhere in this application; better to refuse than to
  store a number no screen can explain.
- **The toggle is per service section**, alongside every other payment setting on that card, so a shop
  can split its alterations payment without seeing split rows on services nobody split.
- **"None" is now "Skip deposit".** It only ever appears where a deposit is being chosen, and that is
  what choosing it means.

**Upgrading from v3.x is automatic and changes nothing on its own.** The first launch adds
`Orders.PaymentSplitsJson`, which is empty for every existing order — read back as "no section is
split", which is the single-method arithmetic those orders already had. Nothing is recalculated and no
receipt changes.

### v3.2.0 — 2026-07-30

**Store Management** — a new administrator-only panel, reached from an enlarged Select Shop screen.

- **Take a store out of service, or put it back.** Reversible and non-destructive: the store keeps every
  order and simply stops appearing in Select Shop. This is deliberately *not* behind the confirmation
  gate — an administrator closing a branch for the season should not have to reach for the dangerous
  tool to do it.
- **Select one store, several, or all** — ctrl+click adds one, shift+click takes a run — and act on the
  whole selection.
- **Download the selected stores' data** to a single file: their orders, their measurement terms and
  their receipt branding. **Restore** reads one back; the file is validated and its contents reported
  before anything is written, and a store already present is left untouched rather than merged or
  duplicated.
- **Delete stores, or reinitialize the installation.** Both are gated behind a phrase generated fresh
  each time that has to be typed exactly — an OK button gets clicked from muscle memory and a fixed word
  gets typed from it, whereas a phrase that must be *read off the screen* forces the eye past the list of
  what is about to go. Both buttons stay disabled until it matches, and either one can save the records
  to a file first. Reinitializing keeps login accounts, the language and global settings, so nobody is
  locked out of their own installation.
- **One-click demo store** on Select Shop, built from the shipped defaults — somewhere to start without
  filling in the setup form. No sample orders are invented.

Quality gates: build **0 warnings / 0 errors**, no SonarLint issue on a changed file, and **1377
assertions across 23 harnesses**, all passing.

### v3.1.0 — 2026-07-30

**A refused save now tells you what is wrong, where, and at the moment it refuses.** The order editor
had eleven validation checks and no rule behind how any of them reported: five raised a dialog, two
wrote a message under the field they were about, and all of them set a summary line at the *foot* of a
form taller than the window — where the eye that just clicked Save never lands. A missing customer name
produced no dialog and no message anywhere near the box.

- **Three surfaces, one code path.** A banner above the form (outside the scrolling area, so it cannot
  scroll away) lists everything that is wrong; a red line under each offending input says which field;
  and a single dialog makes sure the refusal cannot be missed. Every check reports through one place, so
  a new one cannot forget a surface.
- **Missing fields are reported together, not one per attempt.** Clear the customer name and the phone
  number and both are named, both marked. Previously the form stopped at the first, so the second rule
  was only discovered after fixing the first.
- **Corrections are acknowledged where they are made.** Typing into a flagged field clears its message
  immediately rather than leaving it red until the next save, and a cancel/return reason's message
  disappears with the row when the status changes.
- Covered: order number, customer name, phone, email (both malformed and required-for-e-transfer),
  shipping address, and the cancel/return reason and its free-text detail. Opening the custom-made
  editor without a customer marks the same fields the same way instead of only raising a dialog.

Quality gates: build **0 warnings / 0 errors**, no SonarLint issue on a changed file, and **1327
assertions across 22 harnesses**, all passing.

### v3.0.1 — 2026-07-30 (hotfix)

- **Fixed: the languages-and-currencies panel offered a currency it showed no tick box for.** The
  currency rows behind the panel are built from every language installed on the *system*, while the
  cards on the right are grouped by the languages the *shop* runs in. A shop accepting Canadian dollars
  and yen but running only in English and French therefore had yen listed in its preferred-currency
  picker, and saved back into its record, with nothing on screen able to remove it. The panel now
  returns exactly what it shows: a currency none of the shop's languages brings is dropped. No order is
  affected — an order records the currency it was priced in and never reads its shop's.
- **A shop can no longer be left accepting no currency at all.** Clearing the last one now says so
  immediately, in red, beside the buttons rather than in a dialog, and re-ticks the first currency the
  panel offers. It is checked as the panel opens too, so a shop that was already in that state is put
  right rather than refused when it closes.

### v3.0.0 — 2026-07-29

Tax moved onto the axis it actually belongs on: the **store's location**. A shop now says where it
is, and its location decides the standard tax rate its payment methods start from and — the reason
this is a major version — whether its prices are quoted **tax-inclusive**. Both facts reach the
database: a new column on the shop, a new column frozen onto every order.

- **A shop has a location, chosen from a shipped table of tax jurisdictions.** Picking one seeds the
  payment/tax matrix from that jurisdiction's standard rate where it quotes one, so the *lawful*
  configuration is the starting point rather than something to remember to set — and a shop created
  and saved straight
  through is seeded too, not only one whose owner re-picks a location by hand. Tax is a function of
  where the store is, not of the language it runs in or of how a customer pays, so it is its own
  setting — not inferred from the installed languages the way currency is.
- **Tax-inclusive markets are handled as their own pricing mode, not a display toggle.** In China,
  Japan and the EU prices already contain the tax, so the money split **backs the tax out** of the
  price (`amount − amount ÷ (1 + rate)`) instead of adding it on top. The tax is still computed and
  printed — inclusive does not mean invisible, it means embedded, and the receipt names it "tax
  included" rather than "received tax" so nobody adds it to a total that already contains it. Canada
  and the US keep adding tax at settlement, exactly as before.
- **In an inclusive market the jurisdiction's rate is the only rate.** A value-added tax is a
  property of the sale, not of how it was settled — a cash sale in Tokyo carries the same consumption
  tax as a card one — so Shop Settings replaces its per-method matrix with that single rate and
  states it on screen. Both portions of an order, deposit and final balance, use it.
- **Where a jurisdiction quotes a standard rate, that rate is the default.** Japan seeds 10%, France
  20%, and so on — every payment method taxable at that rate out of the box, so making one tax free
  (a cash discount, say) is a deliberate opt-out a shop chooses rather than the default it inherits.
- **The tax-exclusive markets quote none, and that now includes Canada.** Canada and the United
  States both add sales tax separately at settlement, and what a store actually collects turns on
  where in the province or state it sits and on what it sells — so no rate is shipped for either.
  Picking Canada or the US seeds every payment method **tax free** and the shop enters the rate it is
  registered to collect. Changing location asks first if it would discard rules somebody had
  configured, which matters more now that a shop's rate is its own to keep.
- **Canada is one entry, not one per province — but regions still work.** Three provincial rows that
  no longer quote a rate differ in nothing but their name, so the picker offers *Canada*. The region
  machinery is untouched: a code is free-form, a region is written `CA-ON`, and re-adding one is a
  line of JSON plus a label that is already translated in all five language files. Shops saved under
  the old provincial codes are not rewritten — they resolve to their **country** entry and would snap
  back to their province the day it returns.
- **Presets are shipped data, editable without a rebuild.** They live in
  `Settings/System/Defaults/tax-jurisdictions.json` alongside the language tables, so a rate a
  government changes is a one-line file edit, not a code release — including the rate quoted in the
  picker, which each language file carries as a `{0}` rather than spelling out, so no translation goes
  stale. A jurisdiction added there needs no code change; only its display name is translated.
- **The tax number is called what the shop's location calls it, and is only asked for where one
  exists.** "GST/HST" used to be written into the field label, the branding editor and the receipt line
  in all five languages, so a shop in Osaka read *GST/HST* on its own tax slip. A jurisdiction now
  declares which number its businesses are issued — a GST/HST number in Canada, wherever in it a shop
  sits, an EU VAT number for France and Spain, a taxpayer ID in China, a qualified-invoice number in Japan —
  and the United States, which issues no federal equivalent, is not asked at all. A number already
  stored keeps printing under a generic label rather than disappearing if a shop relocates.
- **The pricing mode is frozen onto each order**, exactly as its currency already is. A receipt
  reprinted after a shop relocates, or after a rate changes, still reads as it was charged.
- **A phone number carries the country it belongs to.** Every field that collects one — the customer on
  an order, the customer on a custom-made record, the shop's own number, and both staff screens — puts
  the international dial code in front of it, in a picker showing each country's flag, and checks the
  number against that country's national length (Canada and the US 10, China 11, Japan 10 or 11, France
  and Spain 9). The country is per NUMBER, not per shop: a Toronto shop takes a visiting customer's
  Shanghai mobile by switching the picker, which a rule keyed on the store would have refused. The
  shop's **location** decides only what the field opens on, with its currency as the fallback for a shop
  that has never said where it is. Rules live in
  `Settings/System/Defaults/phone-countries.json`, editable without a rebuild like the tax presets.
  Storage is unchanged — one column holding `+1 905-401-6667` — and **the stricter rule applies to new
  records only**, so nothing already saved starts refusing to save over a number typed before it
  existed.

**Upgrading from v2.x is automatic.** The first launch adds `Shops.LocationCode` and
`Orders.PricesIncludeTax`, gives every existing shop a location inferred from its currency (CNY → CN,
JPY → JP, EUR → FR, USD → US, CAD → the Ontario home market), and leaves every existing order
tax-exclusive so its stored figures do not move. Nothing is asked of the user. A shop that is later
deliberately located somewhere its currency would not imply is never overwritten: the inference is
pinned to the arrival of the column, not run on every launch.

Quality gates: build **0 warnings / 0 errors**, every SonarLint issue on a changed file cleared, and
**1272 assertions across 21 harnesses**, all passing.

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
