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
    // Order status and what follows from it: the picked-up tick, the cancel/return reason, clearing every balance at once, and the checks that gate them.

    // Quick-operation "picked up" toggle: ticking it forces the order status to
    // Completed and locks the status dropdown; unticking reverts the status to
    // Processing and unlocks it. A manual change of the status dropdown to
    // Completed ticks this box in return. A dedicated guard prevents the two
    // handlers from re-triggering each other.
    private void OnPickedUpChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingStatus)
            return;

        // Ask before completing an order that has an unpriced service, and undo the tick if
        // the user backs out. Reverting runs inside the guard so this handler is not re-entered.
        if (PickedUpCheck.IsChecked.GetValueOrDefault() && !ConfirmPickUp())
        {
            _syncingStatus = true;
            try
            {
                PickedUpCheck.IsChecked = false;
            }
            finally
            {
                _syncingStatus = false;
            }
            return;
        }

        _syncingStatus = true;
        try
        {
            if (PickedUpCheck.IsChecked.GetValueOrDefault())
            {
                SelectStatus(OrderStatus.Completed);
                StatusBox.IsEnabled = false;
            }
            else
            {
                SelectStatus(OrderStatus.Processing);
                StatusBox.IsEnabled = true;
            }
        }
        finally
        {
            _syncingStatus = false;
        }
    }

    private void OnStatusChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingStatus)
            return;

        _syncingStatus = true;
        try
        {
            var tag = (StatusBox.SelectedItem as ComboBoxItem)?.Tag as OrderStatus?;
            var isCompleted = tag is OrderStatus.Completed;
            PickedUpCheck.IsChecked = isCompleted;
            StatusBox.IsEnabled = !isCompleted;

            var refunded = tag is OrderStatus.Cancelled or OrderStatus.Returned;
            if (refunded != _isRefunded)
            {
                _isRefunded = refunded;
                ApplyRefundLockState();
            }

            UpdateStatusReasonVisibility();
        }
        finally
        {
            _syncingStatus = false;
        }
    }

    // Shows/hides the return/cancel reason category picker for Cancelled/Returned statuses,
    // swaps its placeholder + label between the return and cancel wording, and defaults the
    // category to the first preset (per the convention: never leave a picker unselected).
    private void UpdateStatusReasonVisibility()
    {
        var tag = (StatusBox.SelectedItem as ComboBoxItem)?.Tag as OrderStatus?;
        var show = tag is OrderStatus.Cancelled or OrderStatus.Returned;

        StatusReasonLabelPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        StatusReasonCategoryBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        // A reason message must not outlive the reason row. Leaving one behind puts red text under a
        // control that is no longer there, describing a rule that no longer applies.
        if (!show)
        {
            SetFieldError(StatusReasonCategoryErrorText, null);
            SetFieldError(StatusReasonErrorText, null);
        }

        if (show && StatusReasonCategoryBox.SelectedIndex < 0)
            StatusReasonCategoryBox.SelectedIndex = 0;

        StatusReasonHint.Text = _localization[tag == OrderStatus.Cancelled
            ? "OrderEdit.Placeholder.CancelReason"
            : "OrderEdit.Placeholder.ReturnReason"];

        UpdateOtherReasonRowVisibility(show);
    }

    // The free-text "Other" reason row only shows alongside the category picker AND only
    // when the selected preset category is "Other".
    private void UpdateOtherReasonRowVisibility(bool categoryRowVisible)
    {
        var isOther = categoryRowVisible
            && (StatusReasonCategoryBox.SelectedItem as ComboBoxItem)?.Tag as string == OtherStatusReasonTag;

        StatusReasonContainer.Visibility = isOther ? Visibility.Visible : Visibility.Collapsed;
        if (isOther)
            StatusReasonHint.Visibility = string.IsNullOrEmpty(StatusReasonBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnStatusReasonCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        // Picking a category answers the "choose a reason" message, so it goes.
        SetFieldError(StatusReasonCategoryErrorText, null);
        UpdateOtherReasonRowVisibility(StatusReasonCategoryBox.Visibility == Visibility.Visible);
    }

    // Selects the matching preset ComboBoxItem for a loaded order. Legacy records saved
    // before the preset picker existed (or an unrecognized/blank category) fall back to
    // "Other" so their existing free-text StatusReason stays visible and editable.
    private void LoadStatusReasonCategory(string? category)
    {
        var matched = false;
        foreach (var item in StatusReasonCategoryBox.Items.OfType<ComboBoxItem>())
        {
            var isMatch = string.Equals(item.Tag as string, category, StringComparison.Ordinal);
            item.IsSelected = isMatch;
            matched |= isMatch;
        }

        if (!matched)
        {
            foreach (var item in StatusReasonCategoryBox.Items.OfType<ComboBoxItem>())
                item.IsSelected = string.Equals(item.Tag as string, OtherStatusReasonTag, StringComparison.Ordinal);
        }
    }

    private void OnStatusReasonTextChanged(object sender, TextChangedEventArgs e)
        => StatusReasonHint.Visibility = string.IsNullOrEmpty(StatusReasonBox.Text) ? Visibility.Visible : Visibility.Collapsed;

    private void SelectStatus(OrderStatus status)
    {
        foreach (ComboBoxItem item in StatusBox.Items)
        {
            if (item.Tag is OrderStatus tag && tag == status)
            {
                StatusBox.SelectedItem = item;
                break;
            }
        }
    }

    private void OnClearAllBalancesChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPayment)
            return;

        var clearAll = ClearAllBalancesCheck.IsChecked.GetValueOrDefault();

        _syncingPayment = true;
        try
        {
            ApplyClearAllToSection(clearAll, _alterationControls);
            ApplyClearAllToSection(clearAll, _customMadeControls);
            ApplyClearAllToSection(clearAll, _clothingControls);
        }
        finally
        {
            _syncingPayment = false;
        }

        UpdatePaymentVisibility();
        RefreshComputedTotals(runAutoComplete: false);

        if (clearAll)
            WarnAboutUnpricedServices();
    }

    // Settling every balance marks each participating service BOTH deposit-received and
    // balance-cleared. A service takes part only when it carries order items: an empty
    // section stays out of the payment flow entirely, while a section that has items but no
    // chosen payment method falls back to cash, and one priced at zero still takes part
    // (flagged afterwards, never blocked — a zero price is sometimes deliberate).
    private static void ApplyClearAllToSection(bool clearAll, PaymentSectionControls c)
    {
        if (!clearAll)
        {
            c.BalanceClearedCheck.IsChecked = false;
            return;
        }

        if (!c.HasItems())
            return;

        var downMethod = GetSelectedDownMethod(c);
        if (downMethod is null)
        {
            downMethod = PaymentMethod.Cash;
            SetSelectedDownMethod(c, PaymentMethod.Cash);
        }

        // "None" means no deposit was taken, so there is nothing to confirm as received and
        // the whole charge falls to the final balance.
        var noDeposit = downMethod == PaymentMethod.None;
        if (!noDeposit)
            c.DownCompletedCheck.IsChecked = true;

        // Default the final balance to the deposit method ONLY when the user hasn't already
        // picked one. A manually forced final method (e.g. deposit by card, final by cash)
        // must be respected instead of being reset to the deposit's way.
        if (GetSelectedFinalMethod(c) is null)
            SetSelectedFinalMethod(c, noDeposit ? PaymentMethod.Cash : downMethod);

        c.BalanceClearedCheck.IsChecked = true;
    }

    // Names of every service that carries items but no charge, as a localized list. Empty
    // when every service that takes part is priced.
    private string UnpricedServiceList()
    {
        var unpriced = AllSections
            .Where(c => c.HasMissingPrice)
            .Select(c => _localization[c.ServiceNameKey])
            .ToList();

        return unpriced.Count == 0
            ? string.Empty
            : _localization.JoinList(unpriced);
    }

    // A service carrying items but no charge is allowed — shops zero-rate one on purpose
    // often enough — so this only tells the user, it never blocks settling the order.
    private void WarnAboutUnpricedServices()
    {
        var unpriced = UnpricedServiceList();
        if (unpriced.Length == 0)
            return;

        MessageBox.Show(
            _localization.Format("OrderEdit.Warn.UnpricedServices", unpriced),
            _localization[ValidationTitleKey],
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    // Marking an order picked up completes it, and a completed order opens read-only from then
    // on. That is worth stopping for when a service went out without a charge, so the shop can
    // catch a missing price while the order can still be edited. Returns false to cancel the
    // tick. A fully priced order is not interrupted.
    private bool ConfirmPickUp()
    {
        var unpriced = UnpricedServiceList();
        if (unpriced.Length == 0)
            return true;

        return MessageBox.Show(
            _localization.Format("OrderEdit.Confirm.PickUpUnpriced", unpriced),
            _localization[ValidationTitleKey],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private bool IsOrderBalanceCleared()
    {
        // A brand-new/empty order starts as outstanding. The gate is order ITEMS, not money:
        // a service priced at zero still takes part, so an order made only of zero-priced
        // items can still be settled (it is flagged as unpriced, not blocked). Gating on
        // _totalAmount here would make the "clear all balances" tick spring straight back off.
        if (!_alterationControls.HasItems() && !_customMadeControls.HasItems() && !_clothingControls.HasItems())
            return false;

        // Cleared only when every charged section is settled; empty sections count as cleared.
        // The deposit is pre-tax, so compare it against the pre-tax subtotal base.
        var alterationCleared = IsSectionCleared(_alterationSubtotal,
            ParseDecimalOrZero(AlterationDownpaymentBox.Text), AlterationBalanceClearedCheck.IsChecked.GetValueOrDefault());
        var customMadeCleared = IsSectionCleared(_customMadeSubtotal,
            ParseDecimalOrZero(CustomMadeDownpaymentBox.Text), CustomMadeBalanceClearedCheck.IsChecked.GetValueOrDefault());
        var clothingCleared = IsSectionCleared(_clothingSubtotal,
            ParseDecimalOrZero(ClothingDownpaymentBox.Text), ClothingBalanceClearedCheck.IsChecked.GetValueOrDefault());
        return alterationCleared && customMadeCleared && clothingCleared;
    }

    private static bool IsSectionCleared(decimal sectionTotal, decimal downpayment, bool balanceCleared)
    {
        if (sectionTotal <= 0m)
            return true;
        if (balanceCleared)
            return true;
        return downpayment >= sectionTotal;
    }

    /// <summary>
    /// Whether every participating section is TICKED as cleared — the master checkbox's own state.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="IsOrderBalanceCleared"/>, which answers a different question: "is
    /// anything still owed". A section whose deposit already covers its total owes nothing, so that
    /// method reports it cleared whatever the tick says — and using it to drive the checkbox made
    /// the checkbox unremovable. This asks only what the user has marked.
    ///
    /// Participation is order ITEMS, matching <see cref="ApplyClearAllToSection"/>, which skips a
    /// section with none: an empty section is not part of the payment flow, and counting it would
    /// leave the master permanently unticked on an order that uses one service.
    /// </remarks>
    private bool AreAllSectionsMarkedCleared()
    {
        var participating = new[] { _alterationControls, _customMadeControls, _clothingControls }
            .Where(section => section.HasItems())
            .ToList();

        return participating.Count > 0
               && participating.TrueForAll(section => section.BalanceClearedCheck.IsChecked is true);
    }
}
