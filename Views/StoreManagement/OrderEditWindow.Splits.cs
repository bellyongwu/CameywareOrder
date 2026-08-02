using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CameywareOrder.Views;

public partial class OrderEditWindow
{
    // Splitting one payment stage across several payment types (v4.0): building the rows, keeping the allocation balanced against the stage total, and reading it back onto the order.

    // ── Splitting a stage across payment types ───────────────────────────────────────────────────

    /// <summary>
    /// Builds one amount row per configurable payment method, for both stages of every section.
    /// </summary>
    /// <remarks>
    /// Driven from <c>PaymentTaxRules.ConfigurableMethods</c>, so the rows are exactly the methods the
    /// shop can configure and adding one needs no change here or in the markup. The legacy
    /// <c>PaymentMethod.Card</c> is not among them, and "None" is the absence of a payment rather than
    /// a way of paying — in a split it is expressed by leaving every box empty.
    /// </remarks>
    private void BuildSplitRows()
    {
        foreach (var section in AllPaymentSections)
        {
            Fill(section.DepositSplitRows, section.DepositRows);
            Fill(section.FinalSplitRows, section.FinalRows);

            // The default lives HERE rather than as IsChecked in the markup: set there it fires the
            // Checked handler during InitializeComponent, against controls that do not exist yet.
            //
            // BOTH pairs. The balance stage's copy was left unset, so that toggle opened with neither
            // option chosen — the card said nothing about how the balance would be taken until somebody
            // clicked one. "No split" is the answer every section starts from, at either stage.
            section.NoSplitRadio.IsChecked = true;
            section.FinalNoSplitRadio.IsChecked = true;
        }

        // Everything the payment handlers touch now exists.
        _sectionsReady = true;

        void Fill(Panel host, List<SplitRow> rows)
        {
            host.Children.Clear();
            rows.Clear();

            foreach (var method in PaymentTaxRules.ConfigurableMethods)
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 14, 6) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

                var label = new TextBlock
                {
                    Text = _localization[$"PaymentMethod.{method}"],
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var amount = new TextBox { Padding = new Thickness(6, 4, 6, 4) };
                amount.PreviewTextInput += OnDecimalTextBoxPreviewTextInput;
                amount.TextChanged += OnSplitAmountChanged;
                amount.GotKeyboardFocus += OnSplitAmountFocused;
                amount.LostFocus += OnSplitAmountCommitted;

                // The placeholder sits BEHIND the box in the same cell, which is how the status-reason
                // field already does it: a TextBox has no placeholder of its own, and a hint drawn
                // beside the box would read as a value somebody had typed.
                var placeholder = new TextBlock
                {
                    Margin = new Thickness(7, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = SplitPlaceholderBrush,
                    IsHitTestVisible = false,
                };

                var field = new Grid { Margin = new Thickness(0, 0, 12, 0) };
                field.Children.Add(amount);
                field.Children.Add(placeholder);
                Grid.SetColumn(field, 1);

                var detail = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = SplitRowTaxBrush,
                };
                Grid.SetColumn(detail, 2);

                grid.Children.Add(label);
                grid.Children.Add(field);
                grid.Children.Add(detail);
                host.Children.Add(grid);

                rows.Add(new SplitRow(method, amount, detail, placeholder));
            }
        }
    }

    /// <summary>What a stage's split still has to account for: its target, less what is allocated.</summary>
    private static decimal SplitShortfall(PaymentSectionControls c, bool finalStage, decimal target)
        => target - (finalStage ? c.FinalRows : c.DepositRows).Sum(row => row.Value);

    /// <summary>
    /// Whether a split deposit's rows add up to the deposit, which is what lets it be marked received.
    /// </summary>
    /// <remarks>
    /// Ticking "received" is the shop saying the money is in hand and moving on: the deposit rows
    /// disappear and the balance stage opens. Allowing that over an allocation that does not balance
    /// stores a deposit whose payment types add up to something else, and the stage it came from is no
    /// longer on screen to correct it. Refusing at SAVE was not enough — by then the evidence is gone.
    ///
    /// Always true for a section that is not split, and where the price already contains the tax:
    /// there is nothing to balance in either case.
    ///
    /// Consulted from <c>ApplySectionLock</c> rather than assigned from the refresh pass. The checkbox's
    /// enabled state has ONE owner, and that method assigns it unconditionally — a gate written
    /// anywhere else is simply overwritten a moment later, which is what the first attempt did.
    /// </remarks>
    private bool IsSplitDepositBalanced(PaymentSectionControls c)
    {
        if (!c.IsDepositSplit || PricesIncludeTax)
            return true;

        return SplitShortfall(c, finalStage: false, SectionMoney(c).Deposit) == 0m;
    }

    /// <summary>
    /// Every split stage must account for exactly what that stage owes, or the order is refused.
    /// </summary>
    /// <remarks>
    /// A shortfall is a PARTIAL payment, and there is no such state anywhere in this application — not
    /// on the order, not on the receipt, not in the balance column — so accepting one would store a
    /// number no screen could explain. An over-allocation is refused for the same reason from the other
    /// side: money taken that the section does not owe.
    ///
    /// Only the stage that is CURRENTLY on screen is checked. The final stage's rows are not visible,
    /// and cannot have been filled in, until the deposit is marked received — holding a shop to an
    /// allocation of a balance it has not reached yet would make the deposit unsaveable.
    /// </remarks>
    private bool ValidateSplitAllocations()
    {
        foreach (var c in AllPaymentSections)
        {
            if (PricesIncludeTax || c.IsServiceSwitchedOff)
                continue;

            var money = SectionMoney(c);
            var finalStage = c.DownCompletedCheck.IsChecked is true;

            // Only the stage that is on screen, and only if THAT stage is the split one — the deposit
            // and the balance answer separately now.
            if (!c.IsSplitAt(finalStage))
                continue;

            var target = finalStage ? money.FinalBase : money.Deposit;
            var shortfall = SplitShortfall(c, finalStage, target);

            if (shortfall == 0m)
                continue;

            // Short and over are different problems and need different sentences. One message with an
            // absolute value told a shop that had allocated 1200 against 600 that "600 is not allocated
            // to a payment type", which is the opposite of what happened.
            var message = _localization.Format(
                shortfall > 0m ? "OrderEdit.Validate.SplitUnbalanced" : "OrderEdit.Validate.SplitOverpaid",
                _localization[c.ServiceNameKey], FormatCurrency(Math.Abs(shortfall)));

            RecordValidationFailure(new[] { message });
            (finalStage ? c.FinalRows : c.DepositRows)[0].Amount.Focus();
            return false;
        }

        return true;
    }

    /// <summary>The money split a section is currently showing, for a check that runs outside a refresh.</summary>
    private SectionPayment SectionMoney(PaymentSectionControls c)
    {
        if (ReferenceEquals(c, _alterationControls))
            return _alterationMoney;

        return ReferenceEquals(c, _customMadeControls) ? _customMadeMoney : _clothingMoney;
    }

    /// <summary>Freezes each section's split onto the order at save.</summary>
    /// <remarks>
    /// Written for EVERY section, including the ones with the toggle off — <c>SetPaymentSplits</c>
    /// stores null when nothing is split, so an order that has never used the feature keeps an empty
    /// column rather than carrying three empty objects around.
    /// </remarks>
    private void ApplyPaymentSplits(Order order)
    {
        var splits = new OrderPaymentSplits();

        Capture(OrderPaymentSplits.AlterationKey, _alterationControls);
        Capture(OrderPaymentSplits.CustomMadeKey, _customMadeControls);
        Capture(OrderPaymentSplits.ClothingKey, _clothingControls);

        order.SetPaymentSplits(splits);

        void Capture(string key, PaymentSectionControls c)
        {
            var section = splits.For(key);
            section.DepositEnabled = c.IsDepositSplit;
            section.FinalEnabled = c.IsFinalSplit;
            section.Deposit = ReadSplitLines(c, finalStage: false).ToList();
            section.Final = ReadSplitLines(c, finalStage: true).ToList();
        }
    }

    /// <summary>Puts a saved order's splits back on screen: the toggle, then each method's amount.</summary>
    /// <remarks>
    /// Under the payment guard, like every other control this window fills from a saved order: setting
    /// a radio or a text box raises the handlers that recompute the totals, and doing that while the
    /// rest of the form is still being populated reads half a form.
    /// </remarks>
    private void LoadPaymentSplits(Order order)
    {
        var splits = order.PaymentSplits;

        Restore(OrderPaymentSplits.AlterationKey, _alterationControls);
        Restore(OrderPaymentSplits.CustomMadeKey, _customMadeControls);
        Restore(OrderPaymentSplits.ClothingKey, _clothingControls);

        void Restore(string key, PaymentSectionControls c)
        {
            var section = splits.For(key);
            c.SplitRadio.IsChecked = section.DepositEnabled;
            c.NoSplitRadio.IsChecked = !section.DepositEnabled;
            c.FinalSplitRadio.IsChecked = section.FinalEnabled;
            c.FinalNoSplitRadio.IsChecked = !section.FinalEnabled;

            Fill(c.DepositRows, section.Deposit);
            Fill(c.FinalRows, section.Final);
        }

        static void Fill(List<SplitRow> rows, List<PaymentSplitLine> lines)
        {
            foreach (var row in rows)
            {
                var line = lines.Find(l => l.Method == row.Method);
                row.Amount.Text = line is { Amount: > 0m } ? line.Amount.ToString("0.##") : string.Empty;
            }
        }
    }

    /// <summary>Turning the split on or off re-shapes the card, so everything is recomputed.</summary>
    /// <remarks>
    /// Serves BOTH stages' toggles — the deposit's and the balance's — because recomputing is all
    /// either one has ever done. The stages still answer independently: which of them is split lives
    /// in <c>SectionPaymentSplit.DepositEnabled</c> / <c>FinalEnabled</c>, which
    /// <see cref="RefreshComputedTotals"/> reads back per stage. They were once mirrored, so picking
    /// "split" for a balance re-shaped a deposit that had already been taken; what fixed that was
    /// separating the DATA, not having two handlers with the same body.
    ///
    /// Guarded on <see cref="_sectionsReady"/>, not only on the payment sync flag. A RadioButton whose
    /// <c>IsChecked</c> is set in MARKUP raises Checked while <c>InitializeComponent</c> is still
    /// running — before any of the section controls exist — so the first thing this handler did was
    /// dereference a null and take the whole window down on open. The markup default was removed as
    /// well, and this guard is what stops the next one from doing it again.
    /// </remarks>
    private void OnSplitModeChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPayment || !_sectionsReady)
            return;

        RefreshComputedTotals();
    }

    /// <summary>
    /// A typed amount changes the tax, the totals and the allocation line, so it goes through the same
    /// refresh every other payment input does — never a local update, which is how two figures on one
    /// card come to disagree.
    /// </summary>
    private void OnSplitAmountChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingPayment)
            return;

        RefreshComputedTotals();
    }

    /// <summary>
    /// Writes each row's tax and the stage's allocation line: what is allocated against what is owed,
    /// and what is left.
    /// </summary>
    /// <remarks>
    /// The per-row tax is computed the way the money calculation computes it — the shop's CURRENT rules
    /// decide whether a method is taxed at all, its stored rate decides how much — so a row showing
    /// "0.00" beside a card is telling the truth about a shop that has made cards tax free, rather than
    /// disagreeing with the total underneath.
    /// </remarks>
    private void RefreshSplitStage(PaymentSectionControls c, bool finalStage, decimal target)
    {
        var rows = finalStage ? c.FinalRows : c.DepositRows;
        var summary = finalStage ? c.FinalSplitSummary : c.DepositSplitSummary;
        var rules = PaymentTaxRules.Active;

        var allocated = 0m;
        var tax = 0m;

        foreach (var row in rows)
        {
            var amount = row.Value;
            allocated += amount;

            var rate = rules.IsTaxable(row.Method) ? rules.RateFor(row.Method) : 0m;
            // Rounded exactly as PortionTax rounds it, per line and before summing. This figure is
            // shown to the shop and that one is charged to the customer; they have to be one number.
            var rowTax = MoneyRounding.Round(amount * rate / 100m);
            tax += rowTax;

            // Each line says what it costs the customer, not just its tax: the amount typed is
            // pre-tax, so the figure they are actually asked for at the till is amount + tax and
            // nothing else on the card states it per method.
            row.Detail.Text = amount > 0m
                ? _localization.Format("OrderEdit.Split.RowDetail",
                    FormatTaxRate(rate), FormatCurrency(rowTax), FormatCurrency(amount + rowTax))
                : string.Empty;
        }

        var left = target - allocated;
        ShowRemainderPlaceholders(rows, left);

        // The line above already states the allocation against the target, so this one says what is
        // WRONG: how much is missing, or how much too much. Naming the ceiling again read as a rule
        // rather than as the thing to correct.
        var state = left switch
        {
            > 0m => _localization.Format("OrderEdit.Split.Remaining", FormatCurrency(left)),
            < 0m => _localization.Format("OrderEdit.Split.Overpaid", FormatCurrency(-left)),
            _ => string.Empty,
        };

        summary.Text = _localization.Format("OrderEdit.Split.Summary",
            FormatCurrency(allocated), FormatCurrency(target), FormatCurrency(tax));

        if (state.Length > 0)
            summary.Text += Environment.NewLine + state;

        summary.Foreground = left == 0m ? BalancedSplitBrush : UnbalancedSplitBrush;
    }

    /// <summary>
    /// Offers what is still unallocated as a placeholder in every row that has not been answered yet.
    /// </summary>
    /// <remarks>
    /// A hint, not a value: nothing is charged until somebody puts it in the box. Recomputed on every
    /// keystroke, so the offer is always the target LESS what has been entered — change one row from
    /// 400 to 300 and every empty row is offering 100 before the next character can be typed.
    ///
    /// It disappears once the stage balances, and never appears on an over-allocated stage, where the
    /// honest next move is to take an amount OUT rather than to be offered more.
    /// </remarks>
    private void ShowRemainderPlaceholders(List<SplitRow> rows, decimal left)
    {
        foreach (var row in rows)
        {
            row.Placeholder.Text = row.IsBlank && left > 0m ? FormatCurrency(left) : string.Empty;
            row.Placeholder.Visibility = Show(row.IsBlank);
        }
    }

    /// <summary>
    /// Clicking into an empty row fills it with everything still unallocated.
    /// </summary>
    /// <remarks>
    /// The figure it writes is ordinary editable text, and the rows it does NOT touch are every other
    /// one: a row already carrying an amount is an answer, and a row still empty keeps offering
    /// whatever is left. Typing 300 over an offered 400 therefore leaves 100, which the remaining empty
    /// rows immediately offer in turn — the allocation walks down the list as the shop fills it in.
    ///
    /// An earlier version settled the other empty rows at zero, to balance the stage in one click. That
    /// was wrong in the way that matters: a typed zero is an ANSWER ("nothing was taken this way"), so
    /// writing it on the shop's behalf both stated something nobody had said and stopped those rows
    /// from ever offering the remainder again.
    /// </remarks>
    private void OnSplitAmountFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_syncingPayment || !_sectionsReady || sender is not TextBox box)
            return;

        if (FindSplitSlot(box) is not { } slot || !slot.Row.IsBlank)
            return;

        var left = SplitTargetFor(slot.Section, slot.FinalStage) - slot.Rows.Sum(row => row.Value);
        if (left <= 0m)
            return;

        _syncingPayment = true;
        try
        {
            slot.Row.Amount.Text = left.ToString("0.##", CultureInfo.CurrentCulture);
        }
        finally
        {
            _syncingPayment = false;
        }

        RefreshComputedTotals();
    }

    /// <summary>
    /// Leaving a row that has taken the stage over its target pulls it back to the largest amount that
    /// still fits.
    /// </summary>
    /// <remarks>
    /// The row CORRECTED is the one just edited, which is the one the shop meant to change — the others
    /// are amounts already agreed, and moving those to make room would silently rewrite a payment that
    /// had been recorded correctly.
    ///
    /// On losing focus rather than on each keystroke. Clamping as the digits arrive fights the typist:
    /// "900" against a 400 ceiling would be rewritten at "9" and never reach a second character. While
    /// typing, the summary says what is wrong; leaving the field is what accepts the correction.
    /// </remarks>
    private void OnSplitAmountCommitted(object sender, RoutedEventArgs e)
    {
        if (_syncingPayment || !_sectionsReady || sender is not TextBox box)
            return;

        if (FindSplitSlot(box) is not { } slot)
            return;

        var others = slot.Rows.Where(row => !ReferenceEquals(row, slot.Row)).Sum(row => row.Value);
        var room = SplitTargetFor(slot.Section, slot.FinalStage) - others;

        if (slot.Row.Value <= room)
            return;

        _syncingPayment = true;
        try
        {
            // Never below zero: rows already agreed can add up past the target on their own, and the
            // honest answer for this one is then "nothing left for you".
            slot.Row.Amount.Text = Math.Max(room, 0m).ToString("0.##", CultureInfo.CurrentCulture);
        }
        finally
        {
            _syncingPayment = false;
        }

        RefreshComputedTotals();
    }

    /// <summary>What a stage's rows must add up to: the deposit typed in, or the balance left after it.</summary>
    private decimal SplitTargetFor(PaymentSectionControls c, bool finalStage)
    {
        var money = SectionMoney(c);
        return finalStage ? money.FinalBase : money.Deposit;
    }

    /// <summary>Locks or releases one stage's split: its toggle and every amount in it.</summary>
    private static void SetSplitStageEnabled(PaymentSectionControls c, bool finalStage, bool enabled)
    {
        if (finalStage)
        {
            c.FinalNoSplitRadio.IsEnabled = enabled;
            c.FinalSplitRadio.IsEnabled = enabled;
        }
        else
        {
            c.NoSplitRadio.IsEnabled = enabled;
            c.SplitRadio.IsEnabled = enabled;
        }

        foreach (var row in finalStage ? c.FinalRows : c.DepositRows)
            row.Amount.IsEnabled = enabled;
    }

    /// <summary>
    /// Shows or hides the split controls, and answers whether this section is splitting. Kept apart
    /// from the stage visibility above it because they are two questions — WHICH shape the card is in,
    /// and WHERE in the payment flow it has got to — and folding both into one method pushed it past
    /// the complexity limit.
    /// </summary>
    private static void ApplySplitModeVisibility(
        PaymentSectionControls c, bool addedAtSettlement, bool depositSplit, bool finalSplit)
    {
        // Offered only where tax is ADDED at settlement. Where the price already contains it, splitting
        // the tender cannot move a figure on the screen.
        c.SplitToggle.Visibility = Show(addedAtSettlement);

        // One method or several, never both on screen: choosing "Cash" while also allocating money to
        // three types is a contradiction rather than a choice. Each stage hides only ITS OWN method
        // row — the deposit can be a single cash payment while the balance is split three ways.
        c.DownMethodRow.Visibility = Show(!depositSplit);
        c.FinalMethodRow.Visibility = Show(!finalSplit);
    }
}
