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
    // Why a save was refused, and how it is reported: a banner above the form, an inline message under the offending input, and one dialog. Marking is split from announcing so a harness can drive a refused save without blocking on a MessageBox.

    /// <summary>
    /// Validates, marks every problem in place, and raises ONE dialog if anything is wrong.
    /// </summary>
    /// <remarks>
    /// The dialog lives here and nowhere else, which is what makes the rest of validation testable: a
    /// <c>MessageBox</c> reached from inside a check blocks the thread, so a harness driving Save with
    /// a blank field would hang on a dialog nothing can answer. <see cref="ValidateForSave"/> marks
    /// without announcing; this announces what was marked. It also means one dialog listing every
    /// problem rather than one dialog per field.
    /// </remarks>
    private bool TryValidateForSave(out OrderStatus status)
    {
        if (ValidateForSave(out status))
            return true;

        AnnounceValidationFailure();
        return false;
    }

    /// <summary>The testable half: validates and MARKS the form, and never opens a dialog.</summary>
    private bool ValidateForSave(out OrderStatus status)
    {
        status = default;
        ClearValidationErrors();

        if (!TryRequireFilled(RequiredTextFields()))
            return false;

        // Present but malformed. Both already write their own inline message.
        // ValidatePhoneField has already written the inline message, which names the country and the
        // digits it expects; the banner takes the general one so it stays one line per problem.
        if (!ValidatePhoneField())
        {
            PhoneField.FocusNumber();
            return Fail("OrderEdit.Validate.PhoneInvalid", null, null);
        }

        if (!ValidateEmailField())
            return Fail("OrderEdit.Validate.EmailInvalid", null, EmailBox);

        if (!IsOrderDateAllowed())
            return Fail("OrderEdit.Validate.OrderDateFuture", OrderDateErrorText, OrderDatePicker);

        if (!IsPickupDateAllowed())
            return Fail("OrderEdit.Validate.PickupDateBeforeOrder", PickupDateErrorText, PickupDatePicker);

        RefreshComputedTotals();

        if (HasPaymentMethodRequiringEmail() && string.IsNullOrWhiteSpace(EmailBox.Text))
            return Fail("OrderEdit.Validate.EmailRequired", EmailErrorText, EmailBox);

        if (_totalAmount < 0)
            return Fail("OrderEdit.Validate.TotalAmount", null, null);

        if (!ValidateSplitAllocations())
            return false;

        if ((StatusBox.SelectedItem as ComboBoxItem)?.Tag is not OrderStatus selectedStatus)
            return Fail("OrderEdit.Validate.Status", null, StatusBox);

        if (selectedStatus == OrderStatus.Shipped && string.IsNullOrWhiteSpace(AddressBox.Text))
            return Fail("OrderEdit.Validate.AddressRequired", AddressErrorText, AddressBox);

        if (selectedStatus is OrderStatus.Cancelled or OrderStatus.Returned && !ValidateStatusReason())
            return false;

        status = selectedStatus;
        return true;
    }

    /// <summary>
    /// Flags every one of <paramref name="fields"/> that is blank, all at once, and focuses the first.
    /// </summary>
    /// <remarks>
    /// One pass, not fail-fast. Fail-fast could only ever name the first missing field, and "the
    /// customer name and the mobile number are missing" is two facts — a form that discloses them one
    /// save at a time makes the user learn its rules by trial.
    /// </remarks>
    private bool TryRequireFilled(IEnumerable<RequiredTextField> fields)
    {
        var missing = fields.Where(field => field.IsBlank()).ToList();
        if (missing.Count == 0)
            return true;

        foreach (var field in missing)
            SetFieldError(field.Error, field.Message);

        RecordValidationFailure(missing.Select(field => field.Message));
        missing[0].Focus();
        return false;
    }

    // A cancelled/returned order must always carry a reason: a preset category is required
    // (defaulted so this only fails if somehow cleared), and choosing "Other" additionally
    // requires the free-text detail to be filled in.
    private bool ValidateStatusReason()
    {
        var category = (StatusReasonCategoryBox.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrWhiteSpace(category))
            return Fail("OrderEdit.Validate.StatusReasonRequired", StatusReasonCategoryErrorText, StatusReasonCategoryBox);

        if (category == OtherStatusReasonTag && string.IsNullOrWhiteSpace(StatusReasonBox.Text))
            return Fail("OrderEdit.Validate.StatusReasonOtherRequired", StatusReasonErrorText, StatusReasonBox);

        return true;
    }

    /// <summary>
    /// Reports one validation failure on every surface at once, and returns false so a check can be
    /// written as <c>return Fail(...)</c>.
    /// </summary>
    /// <remarks>
    /// The three surfaces answer three different questions and a failure needs all of them: the popup
    /// says something is wrong NOW (the Save button is at the foot of a form taller than the window,
    /// so a message that only appears elsewhere can be missed entirely), the banner says what, and the
    /// inline block says where. Routing every check through here is what stops them diverging — the
    /// previous code had five of eleven checks popping up a dialog and two writing anything under a
    /// field, with no rule behind which.
    ///
    /// <paramref name="inline"/> is null where there is nothing to sit under: a computed total, or a
    /// check whose own validator has already written the message itself.
    /// </remarks>
    private bool Fail(string messageKey, TextBlock? inline, Control? focus)
    {
        var message = _localization[messageKey];

        if (inline is not null)
            SetFieldError(inline, message);

        RecordValidationFailure(new[] { message });
        focus?.Focus();
        return false;
    }

    /// <summary>Adds failures to the banner and to what the dialog will say. No dialog of its own.</summary>
    /// <remarks>
    /// Newline-joined rather than run together with <c>Format.ListSeparator</c>: these are whole
    /// sentences, and "Please enter the customer name, The phone number cannot be empty" reads as a
    /// mistake in every language that capitalises.
    /// </remarks>
    private void RecordValidationFailure(IEnumerable<string> messages)
    {
        _validationProblems.AddRange(messages);

        ValidationBannerText.Text = string.Join(Environment.NewLine, _validationProblems);
        ValidationBanner.Visibility = Visibility.Visible;

        // The foot-of-window line is for a save that THREW; a stale one beside the button would read
        // as a second, unrelated problem.
        ErrorText.Text = string.Empty;
    }

    /// <summary>The one dialog, saying everything that was marked.</summary>
    private void AnnounceValidationFailure()
        => MessageBox.Show(
            string.Join(Environment.NewLine, _validationProblems),
            _localization[ValidationTitleKey],
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    /// <summary>
    /// Wipes the banner and every inline message, so a validation pass reports the form as it is NOW.
    /// </summary>
    /// <remarks>
    /// Without this a field fixed between two attempts keeps its red line, which is worse than never
    /// having shown one: the user corrected the thing they were told about and the form still accuses
    /// them of it.
    /// </remarks>
    private void ClearValidationErrors()
    {
        _validationProblems.Clear();
        ValidationBannerText.Text = string.Empty;
        ValidationBanner.Visibility = Visibility.Collapsed;

        foreach (var block in ValidationErrorBlocks())
            SetFieldError(block, null);
    }

    /// <summary>
    /// Whether the day on the picker is one the shop could actually have taken the order on.
    /// </summary>
    /// <remarks>
    /// The calendar cannot offer a future day (<see cref="InitializeOrderDatePicker"/> caps it), but
    /// the box can be typed into, so the rule is enforced here as well as shown there.
    ///
    /// The comparison is against the day this runs rather than the day the window opened, so a form
    /// left open over midnight is not refused for a date that was legal when it was picked. A date
    /// already ON the order passes whatever it says: only the GraphQL API can put a future date
    /// there, and refusing it would leave that order unsaveable rather than merely odd.
    /// </remarks>
    private bool IsOrderDateAllowed()
    {
        if (OrderDatePicker.SelectedDate is not { } picked)
            return true;

        return picked.Date <= DateTime.Today
            || picked.Date == RecordedOrderDate().ToLocalTime().Date;
    }

    /// <summary>
    /// Whether the pickup day is one the order could be collected on — the order date, or later.
    /// </summary>
    /// <remarks>
    /// Measured against the ORDER DATE, not today (v9.3.3). Two things were wrong with today. The
    /// same day was refused outright, which is the commonest sale in the shop — ready-made stock
    /// handed over at the counter is ordered and collected within the hour. And a back-dated order
    /// could not record the day it was actually collected, so entering last week's paperwork meant
    /// promising a day in the future that had already been and gone.
    ///
    /// What is left is the only rule that is true of every order: it cannot be collected before it
    /// was taken. <see cref="RefreshPickupDateFloor"/> blacks out exactly the days this refuses.
    ///
    /// Blank passes HERE: "you have not filled this in" is a different message from "that day is
    /// before the order", and it is <see cref="RequiredTextFields"/> that says the first one.
    /// Reporting both from one check would name whichever came first and leave the other to the
    /// next attempt.
    ///
    /// A date already ON the order passes whatever it says, for the same reason the order date's
    /// check exempts one: an order whose promised day has passed is exactly the order somebody needs
    /// to open, and refusing to save it would make the overdue ones the ones that cannot be worked.
    /// </remarks>
    private bool IsPickupDateAllowed()
    {
        if (PickupDatePicker.SelectedDate is not { } picked)
            return true;

        return picked.Date >= SelectedOrderDate()
            || picked.Date == _existing?.ExpectedPickupDateLocal?.Date;
    }

    private void OnEmailBoxLostFocus(object sender, RoutedEventArgs e) => ValidateEmailField();

    private void OnPhoneFieldCommitted(object? sender, EventArgs e) => ValidatePhoneField();

    // Requirement 5b - an entered email must be well formed. Empty stays allowed
    // here because the payment flow separately enforces email for e-transfer.
    private bool ValidateEmailField()
    {
        var valid = ContactValidation.IsValidEmail(EmailBox.Text);
        SetFieldError(EmailErrorText, valid ? null : _localization["OrderEdit.Validate.EmailInvalid"]);
        return valid;
    }

    /// <summary>
    /// The number must be a possible one in the country picked for it — unless it is a stored number
    /// nobody has touched.
    /// </summary>
    /// <remarks>
    /// The lenient rule (shape, and 7 to 15 digits) exists for numbers that predate the per-country
    /// length rule: holding those to it would mean an order taken last year could not be saved again
    /// — its status could not be corrected, its balance could not be cleared — until somebody re-typed
    /// a phone number they have no way to verify.
    ///
    /// That argument covers the STORED VALUE and nothing else, which is why the choice is made on
    /// <see cref="PhoneNumberField.HasBeenEdited"/> and not on whether the order is new. Keying it to
    /// the order meant an existing one accepted ANY 7-to-15-digit number in any country: a probe
    /// across every shipped country from 6 to 13 digits found the two rules disagreeing on every
    /// length but the correct one, and the lenient answer winning every time. A number typed just now
    /// is typed with the customer standing there, whatever order it belongs to.
    /// </remarks>
    private bool ValidatePhoneField()
    {
        // The rule lives on the control, so this window and the custom-made record editor cannot
        // drift apart — the field is hosted by both and used to be checked by only one.
        var valid = PhoneField.IsAcceptable;
        var message = PhoneField.HasBeenEdited || _existing is null
            ? PhoneField.ValidationMessage
            : _localization["OrderEdit.Validate.PhoneInvalid"];

        SetFieldError(PhoneErrorText, valid ? null : message);
        PhoneField.MarkInvalid(!valid);
        return valid;
    }

    /// <summary>
    /// Clears a field's own message as soon as it is typed into, so the correction is acknowledged
    /// where it was made rather than at the next Save.
    /// </summary>
    /// <remarks>
    /// Wired in code from one map rather than as five <c>TextChanged</c> attributes in the XAML: the
    /// pairing of a box with its message block already exists here, and a second copy of it in markup
    /// is the thing that goes stale when a field is added. Only clears — it does not re-validate, so
    /// nothing turns red while somebody is halfway through typing an address.
    /// </remarks>
    private void RegisterValidationClearing()
    {
        var pairs = new (TextBox Box, TextBlock Error)[]
        {
            (OrderNumberBox, OrderNumberErrorText),
            (CustomerNameBox, CustomerNameErrorText),
            (EmailBox, EmailErrorText),
            (AddressBox, AddressErrorText),
            (StatusReasonBox, StatusReasonErrorText),
        };

        foreach (var (box, error) in pairs)
            box.TextChanged += (_, _) => SetFieldError(error, null);

        // Not a TextBox either: picking a day IS the correction, so the message clears on the
        // selection rather than on a keystroke.
        OrderDatePicker.SelectedDateChanged += (_, _) => SetFieldError(OrderDateErrorText, null);
        PickupDatePicker.SelectedDateChanged += (_, _) => SetFieldError(PickupDateErrorText, null);

        // The phone is not a TextBox any more, and its message must clear on a change to EITHER half —
        // switching the country is as much a correction as retyping the digits.
        PhoneField.PhoneChanged += (_, _) =>
        {
            SetFieldError(PhoneErrorText, null);
            PhoneField.MarkInvalid(false);
        };
        PhoneField.PhoneCommitted += OnPhoneFieldCommitted;
    }

    private static void SetFieldError(TextBlock target, string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            target.Text = string.Empty;
            target.Visibility = Visibility.Collapsed;
        }
        else
        {
            target.Text = message;
            target.Visibility = Visibility.Visible;
        }
    }
}
