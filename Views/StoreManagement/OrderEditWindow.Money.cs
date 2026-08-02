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
    // What the money on this form adds up to: per-stage tax rates, the breakdown lines, the per-section totals and the payment summary. Every figure comes from Order.CalculateSectionPayment through SectionInput, so the live editor and a saved order cannot disagree.

    // Deposit amount edits may fully cover a section, so re-run the auto-complete pass.
    private void OnDownpaymentAmountChanged(object sender, TextChangedEventArgs e)
    {
        // Changing the deposit invalidates the manual "deposit received" confirmation
        // and forces the final balance to be recalculated.
        if (!_syncingPayment && sender is TextBox box
            && GetDownCompletedCheckForBox(box) is { } completedCheck)
        {
            _syncingPayment = true;
            try
            {
                completedCheck.IsChecked = false;
            }
            finally
            {
                _syncingPayment = false;
            }
        }

        if (!_syncingPayment && sender is TextBox depositBox)
            EnforceDepositCeiling(depositBox);

        RefreshComputedTotals();
    }

    /// <summary>
    /// A deposit can never exceed its section's pre-tax service total. CalculateSectionPayment
    /// already clamps it, but silently — which hides a typo behind numbers that quietly stop
    /// responding. This tells the shop what happened and pins the deposit to the total, so the
    /// entered value and the calculated one always agree.
    /// </summary>
    private void EnforceDepositCeiling(TextBox depositBox)
    {
        // A re-entrancy guard of its own: writing the corrected value raises TextChanged again,
        // and a modal dialog pumps messages, so without this the warning can stack up.
        if (_enforcingDepositCeiling)
            return;

        var section = Array.Find(AllSections, c => c.DownpaymentBox == depositBox);
        if (section is null)
            return;

        // Nothing to cap against until the service is priced.
        var subtotal = section.SectionSubtotal();
        if (subtotal <= 0m || ParseDecimalOrZero(depositBox.Text) <= subtotal)
            return;

        _enforcingDepositCeiling = true;
        try
        {
            MessageBox.Show(
                _localization.Format("OrderEdit.Warn.DepositExceedsTotal",
                    _localization[section.ServiceNameKey], FormatCurrency(subtotal)),
                _localization[ValidationTitleKey],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _syncingPayment = true;
            try
            {
                depositBox.Text = subtotal.ToString("0.##");
            }
            finally
            {
                _syncingPayment = false;
            }

            depositBox.CaretIndex = depositBox.Text.Length;
        }
        finally
        {
            _enforcingDepositCeiling = false;
        }
    }

    private void RefreshComputedTotals(bool runAutoComplete = true)
    {
        RefreshAlterationTotals();
        RefreshClothingTotals();
        RefreshCustomMadeTotals();
        RefreshAllServicesTotalAmount();

        // Bug 3: editing the price/tax only recomputes amounts; it must not touch
        // the payment method selections. Auto-complete runs only on deposit/method changes.
        if (runAutoComplete)
            AutoCompleteFullyPaidSections();

        // A cleared section is settled, so its pricing inputs (price/tax and the
        // item/record editors that feed the total) must be locked too. Runs last so
        // it wins over the tax-box enabling done inside the Refresh*Totals passes.
        RefreshPricingLocks();
    }

    // A section counts as settled — and therefore locked against further edits — only when
    // it is marked cleared AND actually carries a charge. The charge test matters: a section
    // with no charge reports "cleared" simply because nothing is owed on it, and locking on
    // that alone traps the user. The price inputs would be frozen at zero, so the section
    // could never be given a price to un-clear it, and the deposit radios would stop
    // responding entirely — the section looks dead on reopen.
    private static bool IsSettled(PaymentSectionControls c)
        => c.BalanceClearedCheck.IsChecked is true && c.SectionTotal() > 0m;

    // Centralized lock manager for a single payment section's input controls.
    // All IsReadOnly decisions live here; no Refresh*Totals method touches lock state.
    private void ApplySectionInputLocks(PaymentSectionControls c, TextBox? priceBox)
    {
        var downCompleted = c.DownCompletedCheck.IsChecked is true;
        var sectionLocked = _isReadOnly || _isRefunded || IsSettled(c) || c.IsServiceSwitchedOff;
        var inputsLocked = sectionLocked || downCompleted;

        if (priceBox is not null)
            priceBox.IsReadOnly = inputsLocked;

        // The tax rate has no lock state of its own any more: it is a store-wide rule shown as a
        // fixed value, so there is nothing here that could be typed into.
        c.DownpaymentBox.IsReadOnly = inputsLocked;
    }

    private void RefreshPricingLocks()
    {
        // Re-apply the enable-state locks as well as the read-only ones. These two used to be
        // driven by different triggers: IsReadOnly here on every refresh, IsEnabled only via
        // UpdatePaymentVisibility, which RefreshComputedTotals skips when runAutoComplete is
        // false. Both now depend on values that a plain refresh can change — the alteration
        // category (IsServiceSwitchedOff) and the section total (IsSettled) — so leaving them on
        // separate triggers stranded the deposit radios and checkboxes in a stale state while the
        // price box unlocked correctly. ApplySectionLock only assigns IsEnabled, with no text
        // writes, so calling it here cannot re-enter this method.
        ApplySectionLock(_alterationControls);
        ApplySectionLock(_customMadeControls);
        ApplySectionLock(_clothingControls);

        ApplySectionInputLocks(_alterationControls, AlterationPriceBox);
        // Additional notes belong to the alteration service, so they lock with it.
        AlterationAdditionalNotesBox.IsReadOnly = _isReadOnly || _isRefunded || AlterationServiceSwitchedOff;
        ApplySectionInputLocks(_customMadeControls, priceBox: null);
        ApplySectionInputLocks(_clothingControls, priceBox: null);

        // Section-level controls not captured inside PaymentSectionControls. Same IsSettled
        // rule as above, so a section with no charge keeps its item editors usable — that is
        // the only way to give it a price.
        var customMadeSectionLocked = _isReadOnly || _isRefunded || IsSettled(_customMadeControls);
        AddCustomMadeButton.IsEnabled = !customMadeSectionLocked;
        RemoveCustomMadeButton.IsEnabled = !customMadeSectionLocked;
        RefreshCustomMadeButtonLabel();

        var clothingSectionLocked = _isReadOnly || _isRefunded || IsSettled(_clothingControls);
        AddItemButton.IsEnabled = !clothingSectionLocked;
        SetClothingRowsLocked(clothingSectionLocked);
    }

    private void SetClothingRowsLocked(bool locked)
    {
        foreach (var row in _clothingItemRows)
        {
            row.CategoryBox.IsEnabled = !locked;
            row.UnitPriceBox.IsReadOnly = locked;
            row.PromotionalPriceBox.IsReadOnly = locked;
            row.RemoveButton.IsEnabled = !locked;
        }
    }

    /// <summary>
    /// When a section's deposit fully covers its total (final balance reaches zero),
    /// mirror the deposit method onto the final balance and mark the section cleared.
    /// </summary>
    private void AutoCompleteFullyPaidSections()
    {
        if (_syncingPayment)
            return;

        _syncingPayment = true;
        try
        {
            _alterationAutoCompleted = AutoCompleteSection(_alterationAutoCompleted, _alterationSubtotal, _alterationControls);
            _customMadeAutoCompleted = AutoCompleteSection(_customMadeAutoCompleted, _customMadeSubtotal, _customMadeControls);
            _clothingAutoCompleted = AutoCompleteSection(_clothingAutoCompleted, _clothingSubtotal, _clothingControls);
        }
        finally
        {
            _syncingPayment = false;
        }

        UpdatePaymentVisibility();
        RefreshPaymentSummary();
    }

    private static bool AutoCompleteSection(bool wasAutoCompleted, decimal subtotalBase, PaymentSectionControls c)
    {
        var downMethod = GetSelectedDownMethod(c);
        var downpayment = ParseDecimalOrZero(c.DownpaymentBox.Text);
        var hasRealDownMethod = downMethod is not null && downMethod != PaymentMethod.None;
        // Bug 1: the deposit-received checkbox is manual; auto-fill only reacts to it.
        var depositReceived = c.DownCompletedCheck.IsChecked is true;
        // The deposit is a pre-tax amount, so it fully covers the section when it reaches
        // the pre-tax subtotal (any card tax is added on top and not owed as a balance).
        var fullyPaid = subtotalBase > 0m && downpayment >= subtotalBase && hasRealDownMethod;

        if (fullyPaid && depositReceived)
        {
            // Only on ENTRY into the fully-paid state, never on every refresh. Re-evaluating the
            // condition each pass made the tick impossible to remove: unticking it (or the master
            // "clear all balances") put it straight back on the next time anything recomputed, so a
            // fully-deposited section could never be re-opened. The auto-complete is a convenience
            // for the moment the deposit covers the total — not a rule the user has to keep losing
            // an argument with. `wasAutoCompleted` stays true, so the state is remembered and
            // re-arms only when the deposit or the received tick actually changes.
            if (!wasAutoCompleted)
            {
                SetSelectedFinalMethod(c, downMethod);
                c.BalanceClearedCheck.IsChecked = true;
            }

            return true;
        }

        // Deposit no longer covers the total (or deposit-received was unchecked):
        // reinitialize only what we auto-filled. The deposit-received checkbox stays manual.
        if (wasAutoCompleted)
        {
            SetSelectedFinalMethod(c, null);
            c.BalanceClearedCheck.IsChecked = false;
        }

        // Bug 1: once the deposit is marked received, default the final method to mirror
        // the deposit method until the user changes it.
        if (hasRealDownMethod && depositReceived && GetSelectedFinalMethod(c) is null)
            SetSelectedFinalMethod(c, downMethod);

        return false;
    }

    // The final balance inherits the deposit's payment method until the user explicitly
    // picks one of its own. Without this the section advertises a tax rate it never
    // applies: choosing Card for the deposit shows a rate on the outstanding balance while
    // the untouched final method stays null, leaving that balance untaxed. Mirrors the same
    // "default the final method from the deposit" convention already used by
    // AutoCompleteSection and ApplyClearAllToSection, and an explicit selection always wins.
    private static PaymentMethod? EffectiveFinalMethod(PaymentSectionControls c)
    {
        var chosen = GetSelectedFinalMethod(c);
        if (chosen is not null)
            return chosen;

        var downMethod = GetSelectedDownMethod(c);
        return downMethod == PaymentMethod.None ? null : downMethod;
    }

    // Seeds both stage rates for every section from a saved order. A null final rate means
    // the order predates the per-stage split, so its single stored rate keeps applying to
    // both portions. Must run AFTER LoadPaymentFields: the card/cash rule reads the payment
    // radios, and with none selected yet it would zero a stored rate before it is ever used.
    private void LoadStageTaxRates(Order existing)
    {
        LoadSectionTaxRates(_alterationControls, existing.AlterationTaxRate ?? existing.TaxRate, existing.AlterationFinalTaxRate);
        LoadSectionTaxRates(_clothingControls, existing.ClothingTaxRate ?? existing.TaxRate, existing.ClothingFinalTaxRate);
        LoadSectionTaxRates(_customMadeControls, existing.CustomMadeTaxRate, existing.CustomMadeFinalTaxRate);
    }

    private void LoadSectionTaxRates(PaymentSectionControls c, decimal? depositRate, decimal? finalRate)
    {
        c.DepositTaxRate = depositRate ?? DefaultTaxRate;
        c.FinalTaxRate = finalRate ?? c.DepositTaxRate;
        // Point the display at whichever stage the loaded order is already in.
        c.ShowingFinalRate = c.IsFinalStage;
        ShowStageRate(c);
    }

    /// <summary>
    /// Resolves both stage rates for a section and shows the one that applies now.
    ///
    /// The rate is a STORE rule, not a per-order figure: it comes from
    /// <see cref="PaymentTaxRules.Active"/> keyed on the method settling that portion, which is
    /// what makes a change in Shop Settings take effect across the shop. The one exception is a
    /// read-only order — completed, shipped, cancelled or returned. That one keeps the rates it
    /// was actually charged, because its receipt has already been printed and the screen must not
    /// disagree with the paper.
    ///
    /// A tax-INCLUSIVE order takes one rate for both portions from the jurisdiction instead — see
    /// <see cref="IncludedTaxRatePercent"/> — and never zeroes it per method, because the tax is
    /// already inside the price whatever settles it.
    /// </summary>
    private void ApplyStageTaxRates(PaymentSectionControls c)
    {
        if (PricesIncludeTax)
        {
            if (!_isReadOnly)
            {
                var includedRate = IncludedTaxRatePercent;
                c.DepositTaxRate = includedRate;
                c.FinalTaxRate = includedRate;
            }

            c.ShowingFinalRate = c.IsFinalStage;
            ShowStageRate(c);
            return;
        }

        var rules = PaymentTaxRules.Active;
        var depositMethod = GetSelectedDownMethod(c);
        var finalMethod = EffectiveFinalMethod(c);

        if (_isReadOnly)
        {
            // Still zeroed for a method the shop has since made tax free, so the figures on screen
            // always match what Order.CalculateSectionPayment will compute for the same order.
            if (!rules.IsTaxable(depositMethod))
                c.DepositTaxRate = 0m;
            if (!rules.IsTaxable(finalMethod))
                c.FinalTaxRate = 0m;
        }
        else
        {
            c.DepositTaxRate = rules.RateFor(depositMethod);
            c.FinalTaxRate = rules.RateFor(finalMethod);
        }

        c.ShowingFinalRate = c.IsFinalStage;
        ShowStageRate(c);
    }

    // Writes the current stage's rate into its (read-only) value block and names the stage.
    private void ShowStageRate(PaymentSectionControls c)
    {
        var stageRate = c.ShowingFinalRate ? c.FinalTaxRate : c.DepositTaxRate;
        c.TaxValueText.Text = FormatTaxRate(stageRate);
        UpdateTaxLabel(c);
    }

    private static string FormatTaxRate(decimal ratePercent) => TaxRateFormat.Percent(ratePercent);

    // Small print under Order.Fields.ServiceTotalTax: how the section's tax splits across the two portions
    // and which method settled each, so a $0 line reads as "that portion wasn't card"
    // rather than as a missing charge.
    private void UpdateTaxBreakdownLines(PaymentSectionControls c, SectionPayment money)
    {
        // Read off the split rather than re-derived as `Received − Deposit`: that difference is zero
        // whenever the tax is already inside the price, which printed "tax 0" beside a total that
        // was not zero. SectionPayment carries the per-portion figure for both modes.
        var depositMethod = GetSelectedDownMethod(c);
        c.DepositTaxLine.Text = _localization.Format("Order.Fields.DepositTaxLine",
            PaymentMethodName(depositMethod),
            FormatCurrency(money.DepositTax));
        c.FinalTaxLine.Text = _localization.Format("Order.Fields.FinalTaxLine",
            PaymentMethodName(EffectiveFinalMethod(c)),
            FormatCurrency(money.FinalTax));

        UpdateDueAndReceivedLines(c, money);
        // Called from here rather than from each section's refresh: both panels are then written in
        // one pass, from one reading of the split, for every section — which is the only way the two
        // views of the same order stay in step.
        UpdateInclusiveBreakdown(c, money);

        // Each stage's split allocation, against what that stage actually owes.
        RefreshSplitStage(c, finalStage: false, money.Deposit);
        RefreshSplitStage(c, finalStage: true, money.FinalBase);
    }

    /// <summary>
    /// One section's calculation input, carrying its split lines when that section's card is set to
    /// split — read live off the amount boxes, so the figures move as they are typed.
    /// </summary>
    /// <remarks>
    /// The lines are built from the CURRENT rate for each method rather than from anything stored,
    /// because this is the editor: what the shop is about to charge is what its rules say today. They
    /// are frozen onto the order at save (<c>PaymentSplitLine.RatePercent</c>), which is what keeps a
    /// reprinted receipt honest afterwards.
    /// </remarks>
    private SectionPaymentInput SectionInput(PaymentSectionControls c, decimal subtotal, decimal deposit)
        => new(subtotal, deposit, c.DepositTaxRate, c.FinalTaxRate,
            GetSelectedDownMethod(c), EffectiveFinalMethod(c), PricesIncludeTax)
        {
            DepositSplit = c.IsDepositSplit ? ReadSplitLines(c, finalStage: false) : null,
            FinalSplit = c.IsFinalSplit ? ReadSplitLines(c, finalStage: true) : null,
        };

    /// <summary>
    /// What each portion costs, beside what has actually been taken for it.
    /// </summary>
    /// <remarks>
    /// The DUE figures are the taxed amounts — `ReceivedDownpayment` and `FinalCharge` on the
    /// section split — because that is what the customer is actually asked for; the pre-tax rows
    /// above already say what the work cost. Both are shown from the start.
    ///
    /// A RECEIVED line appears only once its portion is confirmed, and it carries the same figure.
    /// Showing it from the start would state that money had been taken when it had not, and showing
    /// a zero would be worse — indistinguishable from a portion that was genuinely free. Label and
    /// value are hidden together: a lone label reads as a value that failed to load.
    ///
    /// The final balance's received line follows the section's own cleared TICK, not "is anything
    /// owed". A deposit covering the whole total leaves nothing owed, but nothing has been collected
    /// for the final portion either — and it is precisely that case where the two answers diverge.
    /// </remarks>
    private void UpdateDueAndReceivedLines(PaymentSectionControls c, SectionPayment money)
    {
        var depositDue = money.ReceivedDownpayment;
        var balanceDue = money.FinalCharge;

        c.DueDownpaymentText.Text = FormatCurrency(depositDue);
        c.FinalDueDownpaymentText.Text = FormatCurrency(depositDue);
        c.FinalDueBalanceText.Text = FormatCurrency(balanceDue);

        var depositReceived = c.DownCompletedCheck.IsChecked is true;
        c.FinalReceivedDownpaymentText.Text = FormatCurrency(depositReceived ? depositDue : 0m);
        SetLineVisible(c.FinalReceivedDownpaymentLabel, c.FinalReceivedDownpaymentText, depositReceived);

        var balanceReceived = c.BalanceClearedCheck.IsChecked is true;
        c.FinalReceivedBalanceText.Text = FormatCurrency(balanceReceived ? balanceDue : 0m);
        SetLineVisible(c.FinalReceivedBalanceLabel, c.FinalReceivedBalanceText, balanceReceived);

        // The inclusive panel is filled from the SAME figures, in the same pass. Filling it from its
        // own reading of the split is how the two panels would come to disagree about one order.
        c.IncReceivedDepositText.Text = FormatCurrency(depositReceived ? depositDue : 0m);
        SetLineVisible(c.IncReceivedDepositLabel, c.IncReceivedDepositText, depositReceived);
        c.IncDueBalanceText.Text = FormatCurrency(balanceDue);
        c.IncReceivedBalanceText.Text = FormatCurrency(balanceReceived ? balanceDue : 0m);
        SetLineVisible(c.IncReceivedBalanceLabel, c.IncReceivedBalanceText, balanceReceived);
    }

    /// <summary>
    /// The rows the inclusive panel owns alone: the tax-inclusive price, what is still outstanding,
    /// and the line naming the tax already inside that price. Everything else it shows is written by
    /// <see cref="UpdateDueAndReceivedLines"/>, which fills both panels from one reading of the split.
    /// </summary>
    /// <remarks>
    /// Runs whatever the pricing mode: the panel it writes into is collapsed in the other one, and a
    /// guard here would only mean the rows were stale the moment a shop's location changed under an
    /// order being edited. The tax line is skipped when nothing is taxed — "Includes VAT (0%): 0.00"
    /// is noise, and a zero-rated inclusive order is exactly the case where it would appear.
    /// </remarks>
    private void UpdateInclusiveBreakdown(PaymentSectionControls c, SectionPayment money)
    {
        // Same rule as every other residual on this screen: a cleared section owes nothing.
        var residual = c.BalanceClearedCheck.IsChecked is true ? 0m : money.FinalCharge;

        c.IncTotalText.Text = FormatCurrency(money.Subtotal);
        c.IncResidualText.Text = FormatCurrency(residual);

        // Either stage rate would do — they are the same number in this mode — but the tax must
        // actually be non-zero as well, or a section priced at zero would advertise a rate it never
        // charged anything at.
        var rate = c.DepositTaxRate;
        var taxed = money.Tax > 0m && rate > 0m;
        if (taxed)
        {
            c.IncTaxLabel.Text = _localization.Format("Order.Fields.IncludedTaxLabel",
                ShopTaxName, TaxRateFormat.Text(rate));
            c.IncTaxText.Text = FormatCurrency(money.Tax);
        }

        SetLineVisible(c.IncTaxLabel, c.IncTaxText, taxed);
    }

    private static void SetLineVisible(TextBlock label, TextBlock value, bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        label.Visibility = visibility;
        value.Visibility = visibility;
    }

    // Normalized so an order still carrying the legacy single "Card" value reads as Debit Card
    // rather than the retired label.
    private string PaymentMethodName(PaymentMethod? method)
        => _localization[$"PaymentMethod.{PaymentTaxRules.Normalize(method ?? PaymentMethod.None)}"];

    /// <summary>
    /// Names the stage the tax box is showing, so a rate here is never mistaken for the other
    /// portion's — except where the price already contains the tax, which has no stages to tell
    /// apart: a value-added tax is a property of the sale, so the deposit and the final balance
    /// carry the same rate by construction. There the label names the TAX instead ("VAT Rate"),
    /// which is also the only place that rate appears once the deposit-stage breakdown is gone.
    /// </summary>
    private void UpdateTaxLabel(PaymentSectionControls c)
    {
        if (PricesIncludeTax)
        {
            c.TaxLabel.Text = _localization.Format("Order.Fields.IncludedTaxRateLabel", ShopTaxName);
            return;
        }

        c.TaxLabel.Text = _localization[c.ShowingFinalRate
            ? "Order.Fields.FinalTaxRate"
            : "Order.Fields.DepositTaxRate"];
    }

    /// <summary>What this shop's location calls its tax, from its <c>TaxName.*</c> key.</summary>
    private string ShopTaxName => TaxJurisdictions.TaxName(ShopContext.Instance.Current, _localization);

    private void RefreshAlterationTotals()
    {
        // A switched-off alteration service contributes nothing, whatever the price box holds —
        // the value is kept so switching the category back restores it.
        var price = AlterationServiceSwitchedOff ? 0m : ParseDecimalOrZero(AlterationPriceBox.Text);
        // Resolves both stage rates and points the shared tax box at the current stage.
        // Cash/Etransfer portions are forced to 0%, so the displayed rate always matches
        // what is actually charged.
        ApplyStageTaxRates(_alterationControls);
        var downpayment = ParseDecimalOrZero(AlterationDownpaymentBox.Text);
        var money = Order.CalculateSectionPayment(SectionInput(_alterationControls, price, downpayment));
        // A cleared balance means nothing is still owed for this section.
        var residual = AlterationBalanceClearedCheck.IsChecked.GetValueOrDefault() ? 0m : money.FinalCharge;

        _alterationSubtotal = price;
        _alterationSumTotal = money.Total;
        _alterationMoney = money;

        // Deposit-stage rows are scoped to that stage and add up: subtotal + deposit tax — or just
        // the subtotal when the tax is already inside it. SectionPayment owns that rule.
        //
        // The tax row shows the DEPOSIT portion's tax alone; the final portion's joins only at the
        // final stage, whose panel shows the complete figure. Stage-scoping it is what makes the
        // deposit amount visibly move it: a section's TOTAL tax is invariant to the deposit split
        // whenever both portions share a rate (deposit*r + (subtotal−deposit)*r == subtotal*r), so
        // showing the total here made the row look frozen.
        AlterationSubtotalText.Text = FormatCurrency(price);
        // Pre-tax balance still to come: the subtotal less the deposit, before any card tax.
        AlterationPreTaxBalanceText.Text = FormatCurrency(money.FinalBase);
        AlterationDepositTaxText.Text = FormatCurrency(money.DepositTax);
        AlterationSumTotalText.Text = FormatCurrency(money.DepositStageTotal);
        AlterationFinalPriceDisplayText.Text = FormatCurrency(price);
        AlterationFinalDownpaymentDisplayText.Text = FormatCurrency(money.Deposit);
        AlterationFinalPreTaxBalanceText.Text = FormatCurrency(money.FinalBase);
        AlterationFinalTotalTaxText.Text = FormatCurrency(money.Tax);
        UpdateTaxBreakdownLines(_alterationControls, money);
        AlterationFinalTotalText.Text = FormatCurrency(money.Total);
        AlterationResidualText.Text = FormatCurrency(residual);
    }

    private void RefreshClothingTotals()
    {
        decimal subtotal = 0m;
        foreach (var row in _clothingItemRows)
        {
            var rowSubtotal = GetClothingItemSubtotal(row);
            row.SubtotalText.Text = FormatCurrency(rowSubtotal);
            subtotal += rowSubtotal;
        }

        // See RefreshAlterationTotals: resolves both stage rates and retargets the tax box.
        ApplyStageTaxRates(_clothingControls);
        var downpayment = ParseDecimalOrZero(ClothingDownpaymentBox.Text);
        var money = Order.CalculateSectionPayment(SectionInput(_clothingControls, subtotal, downpayment));
        // A cleared balance means nothing is still owed for this section.
        var residual = ClothingBalanceClearedCheck.IsChecked.GetValueOrDefault() ? 0m : money.FinalCharge;

        _clothingSubtotal = subtotal;
        _clothingSumTotal = money.Total;
        _clothingMoney = money;

        // Deposit-stage rows, same rule as RefreshAlterationTotals.
        ClothingPriceText.Text = FormatCurrency(subtotal);
        ClothingSubtotalText.Text = FormatCurrency(subtotal);
        ClothingPreTaxBalanceText.Text = FormatCurrency(money.FinalBase);
        ClothingDepositTaxText.Text = FormatCurrency(money.DepositTax);
        ClothingSumTotalText.Text = FormatCurrency(money.DepositStageTotal);
        ClothingFinalPriceDisplayText.Text = FormatCurrency(subtotal);
        ClothingFinalDownpaymentDisplayText.Text = FormatCurrency(money.Deposit);
        ClothingFinalPreTaxBalanceText.Text = FormatCurrency(money.FinalBase);
        ClothingFinalTotalTaxText.Text = FormatCurrency(money.Tax);
        UpdateTaxBreakdownLines(_clothingControls, money);
        ClothingFinalTotalText.Text = FormatCurrency(money.Total);
        ClothingResidualText.Text = FormatCurrency(residual);
    }

    private void RefreshCustomMadeTotals()
    {
        _customMadeSubtotal = _customMadeRecords.Sum(record => record.Subtotal);
        // See RefreshAlterationTotals: resolves both stage rates and retargets the tax box.
        ApplyStageTaxRates(_customMadeControls);
        var downpayment = ParseDecimalOrZero(CustomMadeDownpaymentBox.Text);
        var money = Order.CalculateSectionPayment(SectionInput(_customMadeControls, _customMadeSubtotal, downpayment));
        _customMadeSumTotal = money.Total;
        _customMadeMoney = money;

        // A cleared balance means nothing is still owed for this section.
        var residual = CustomMadeBalanceClearedCheck.IsChecked.GetValueOrDefault() ? 0m : money.FinalCharge;

        // Deposit-stage rows, same rule as RefreshAlterationTotals.
        CustomMadePriceText.Text = FormatCurrency(_customMadeSubtotal);
        CustomMadeSubtotalText.Text = FormatCurrency(_customMadeSubtotal);
        CustomMadePreTaxBalanceText.Text = FormatCurrency(money.FinalBase);
        CustomMadeDepositTaxText.Text = FormatCurrency(money.DepositTax);
        CustomMadeSumTotalText.Text = FormatCurrency(money.DepositStageTotal);
        CustomMadeFinalPriceDisplayText.Text = FormatCurrency(_customMadeSubtotal);
        CustomMadeFinalDownpaymentDisplayText.Text = FormatCurrency(money.Deposit);
        CustomMadeFinalPreTaxBalanceText.Text = FormatCurrency(money.FinalBase);
        CustomMadeFinalTotalTaxText.Text = FormatCurrency(money.Tax);
        UpdateTaxBreakdownLines(_customMadeControls, money);
        CustomMadeFinalTotalText.Text = FormatCurrency(money.Total);
        CustomMadeResidualText.Text = FormatCurrency(residual);
    }

    private void RefreshAllServicesTotalAmount()
    {
        _totalAmount = _alterationSumTotal + _clothingSumTotal + _customMadeSumTotal;
        TotalAmountText.Text = FormatCurrency(_totalAmount);
        RefreshServicesTotalBreakdown();
        RefreshPaymentSummary();
    }

    // A section's deposit only counts as received once its "deposit received" box is ticked.
    private static decimal SectionReceivedDeposit(SectionPayment money, PaymentSectionControls c)
        => c.DownCompletedCheck.IsChecked is true ? money.ReceivedDownpayment : 0m;

    private void RefreshPaymentSummary()
    {
        var alterationDown = _alterationMoney.Deposit;
        var customMadeDown = _customMadeMoney.Deposit;
        var clothingDown = _clothingMoney.Deposit;

        // Received deposits: nominal deposit plus its card tax, but ONLY for sections whose
        // "deposit received" box has been ticked. Until then the typed amount is what the
        // shop expects to take, not what it holds — mirrors Order.ReceivedDownpayment so the
        // saved order reports the same figure.
        var receivedDownpayment =
            SectionReceivedDeposit(_alterationMoney, _alterationControls)
            + SectionReceivedDeposit(_customMadeMoney, _customMadeControls)
            + SectionReceivedDeposit(_clothingMoney, _clothingControls);

        var alterationCleared = AlterationBalanceClearedCheck.IsChecked.GetValueOrDefault();
        var customMadeCleared = CustomMadeBalanceClearedCheck.IsChecked.GetValueOrDefault();
        var clothingCleared = ClothingBalanceClearedCheck.IsChecked.GetValueOrDefault();

        // Cleared sections no longer contribute to the outstanding final balance.
        var alterationResidual = alterationCleared ? 0m : _alterationMoney.FinalCharge;
        var customMadeResidual = customMadeCleared ? 0m : _customMadeMoney.FinalCharge;
        var clothingResidual = clothingCleared ? 0m : _clothingMoney.FinalCharge;
        var finalBalance = alterationResidual + customMadeResidual + clothingResidual;

        // Received final balance: the taxed final charge collected on every cleared section.
        var receivedFinalBalance =
            (alterationCleared ? _alterationMoney.FinalCharge : 0m)
            + (customMadeCleared ? _customMadeMoney.FinalCharge : 0m)
            + (clothingCleared ? _clothingMoney.FinalCharge : 0m);

        PrepaidDownpaymentText.Text = FormatCurrency(receivedDownpayment);
        SummaryFinalBalanceText.Text = FormatCurrency(finalBalance);
        ReceivedFinalBalanceText.Text = FormatCurrency(receivedFinalBalance);

        // Break down which services still owe an outstanding final balance.
        FinalBalanceBreakdownPanel.Children.Clear();
        AddFinalBalanceDetail("ServiceType.Alterations", alterationResidual);
        AddFinalBalanceDetail("ServiceType.CustomMade", customMadeResidual);
        AddFinalBalanceDetail("ServiceType.ReadyMade", clothingResidual);
        FinalBalanceBreakdownPanel.Visibility = FinalBalanceBreakdownPanel.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var cleared = IsOrderBalanceCleared();
        UpdateBalanceStatusDisplay(cleared);

        // The "picked up" toggle only becomes selectable once the order has at least one
        // charged service and every final balance is cleared (IsOrderBalanceCleared is
        // false while the order total is zero). Keep it enabled while already ticked so a
        // completed order can still be reverted. Read-only or refunded orders stay locked.
        if (_isReadOnly || _isRefunded)
            PickedUpCheck.IsEnabled = false;
        else
            PickedUpCheck.IsEnabled = cleared || PickedUpCheck.IsChecked.GetValueOrDefault();

        // The master follows the section TICKS, not IsOrderBalanceCleared(). The two disagree
        // exactly when a deposit already covers a section's total: nothing is owed, so the order is
        // financially cleared, but the user may still have unticked the box — and driving the master
        // from the money meant it sprang back on the instant anything recomputed, taking the
        // sections with it. The money question and the checkbox are different questions; only the
        // status display and the picked-up gate below use the money one.
        var previousSync = _syncingPayment;
        _syncingPayment = true;
        ClearAllBalancesCheck.IsChecked = AreAllSectionsMarkedCleared();
        _syncingPayment = previousSync;

        // Requirement 3b: indicate payment types with amount in labeling.
        UpdateMethodLabel(AlterationDownMethodLabel, DownpaymentMethodKey,
            GetSelectedDownMethod(_alterationControls), alterationDown);
        UpdateMethodLabel(AlterationFinalMethodLabel, FinalBalanceMethodKey,
            GetSelectedFinalMethod(_alterationControls), alterationResidual);

        UpdateMethodLabel(CustomMadeDownMethodLabel, DownpaymentMethodKey,
            GetSelectedDownMethod(_customMadeControls), customMadeDown);
        UpdateMethodLabel(CustomMadeFinalMethodLabel, FinalBalanceMethodKey,
            GetSelectedFinalMethod(_customMadeControls), customMadeResidual);

        UpdateMethodLabel(ClothingDownMethodLabel, DownpaymentMethodKey,
            GetSelectedDownMethod(_clothingControls), clothingDown);
        UpdateMethodLabel(ClothingFinalMethodLabel, FinalBalanceMethodKey,
            GetSelectedFinalMethod(_clothingControls), clothingResidual);
    }

    private void UpdateMethodLabel(TextBlock label, string baseKey, PaymentMethod? method, decimal amount)
    {
        var text = _localization[baseKey];
        if (method is not null && method != PaymentMethod.None)
            text += $"  ·  {_localization[$"PaymentMethod.{method}"]}  {FormatCurrency(amount)}";
        label.Text = text;
    }

    // Refunded orders show Payment.Status.Refunded in red; otherwise the settled/outstanding
    // label + green/orange colour.
    private void UpdateBalanceStatusDisplay(bool cleared)
    {
        if (_isRefunded)
        {
            BalanceStatusText.Text = _localization["Payment.Status.Refunded"];
            BalanceStatusText.Foreground = System.Windows.Media.Brushes.Red;
            return;
        }

        BalanceStatusText.Text = cleared
            ? _localization["Payment.Status.Cleared"]
            : _localization["Payment.Status.Outstanding"];
        BalanceStatusText.Foreground = cleared
            ? System.Windows.Media.Brushes.Green
            : System.Windows.Media.Brushes.OrangeRed;
    }

    // Small print under Order.Fields.AllServicesTotalAmount: one line per service that is part of this
    // order, showing what it covers and what it costs, e.g. "Alterations (Garment Adjustments): $123". A service
    // qualifies by carrying order items — the same rule the "clear all balances" pass uses —
    // so a section priced at zero is still listed (flagged) rather than silently dropped.
    private void RefreshServicesTotalBreakdown()
    {
        ServicesTotalBreakdownPanel.Children.Clear();
        AddServiceTotalDetail(_alterationControls, AlterationDetailText());
        AddServiceTotalDetail(_customMadeControls, CustomMadeDetailText());
        AddServiceTotalDetail(_clothingControls, ClothingDetailText());
        ServicesTotalBreakdownPanel.Visibility = ServicesTotalBreakdownPanel.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void AddServiceTotalDetail(PaymentSectionControls c, string detail)
    {
        if (!c.HasItems())
            return;

        var name = _localization[c.ServiceNameKey];
        // Punctuation differs per language (fullwidth in Chinese), so the whole label shape
        // lives in the string table rather than being concatenated here.
        var label = string.IsNullOrEmpty(detail)
            ? _localization.Format("Order.Fields.ServiceTotalLineNoDetail", name)
            : _localization.Format("Order.Fields.ServiceTotalLine", name, detail);

        var missingPrice = c.HasMissingPrice;
        if (missingPrice)
            label += _localization["Order.Fields.ServiceTotalUnpriced"];

        ServicesTotalBreakdownPanel.Children.Add(
            BuildBreakdownRow(label, FormatCurrency(c.SectionTotal()), missingPrice));
    }

    // One breakdown line laid out as label + amount. Its first column joins the summary
    // grid's "SummaryLabel" shared-size group, so the label sits under Order.Fields.AllServicesTotalAmount and the
    // amount under that row's figure.
    private static Grid BuildBreakdownRow(string label, string amount, bool highlight)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            SharedSizeGroup = "SummaryLabel"
        });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var brush = highlight ? UnpricedLineBrush : BreakdownLineBrush;

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = brush,
            TextWrapping = TextWrapping.Wrap,
            // Matches the summary labels' right margin so the amounts line up.
            Margin = new Thickness(0, 0, 12, 0)
        };
        Grid.SetColumn(labelText, 0);

        var amountText = new TextBlock
        {
            Text = amount,
            FontSize = 11,
            Foreground = brush
        };
        Grid.SetColumn(amountText, 1);

        row.Children.Add(labelText);
        row.Children.Add(amountText);
        return row;
    }

    private void AddFinalBalanceDetail(string serviceKey, decimal residual)
    {
        if (residual <= 0m)
            return;

        FinalBalanceBreakdownPanel.Children.Add(new TextBlock
        {
            Text = $"·  {_localization[serviceKey]}:  {FormatCurrency(residual)}",
            Foreground = System.Windows.Media.Brushes.Firebrick,
            Margin = new Thickness(0, 2, 0, 0)
        });
    }

    private decimal? GetSubtotalForServiceType(OrderServiceType serviceType)
        => serviceType switch
        {
            OrderServiceType.Alterations => _alterationSubtotal,
            OrderServiceType.ReadyMade => _clothingSubtotal,
            _ => null
        };

    // Feeds the legacy single-rate Orders.TaxRate column, which predates the per-stage
    // split: it carries the deposit rate, matching how the model reads it back
    // (XxxTaxRate ?? TaxRate).
    private decimal? GetTaxRateForServiceType(OrderServiceType serviceType)
        => serviceType switch
        {
            OrderServiceType.Alterations => _alterationControls.DepositTaxRate,
            OrderServiceType.ReadyMade => _clothingControls.DepositTaxRate,
            _ => null
        };
}
