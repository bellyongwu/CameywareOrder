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
    // Input plumbing shared by every money box on the form: digits-only entry, paste filtering, and the select-all-on-focus / restore-zero-on-blur behaviour.

    private void RegisterDecimalTextBoxes()
    {
        // Every money input gets the same treatment: digits-only filtering, paste filtering,
        // and the zero-clearing focus behaviour that stops "0" turning into "012".
        // The alteration price opts out of restore-zero-on-blur: a BLANK price box is what marks
        // the alteration service as absent from the order (HasItems), so turning it into "0"
        // would silently enrol the service as an unpriced one.
        RegisterMoneyBox(AlterationPriceBox, restoreZeroOnBlur: false);
        RegisterMoneyBox(AlterationDownpaymentBox);
        RegisterMoneyBox(ClothingDownpaymentBox);
        RegisterMoneyBox(CustomMadeDownpaymentBox);
    }

    /// <summary>
    /// Wires the shared money-input behaviour. Clothing item rows are created at runtime and
    /// call this too, so every price box in the window behaves identically.
    /// </summary>
    /// <param name="restoreZeroOnBlur">
    /// Pass false for a box where BLANK carries its own meaning and must not become "0" —
    /// an optional promotional price, or the alteration price box whose emptiness marks the
    /// service as absent. The zero-clearing focus behaviour still applies either way.
    /// </param>
    private void RegisterMoneyBox(TextBox box, bool restoreZeroOnBlur = true)
    {
        RegisterDecimalTextBox(box);
        box.GotFocus += OnMoneyBoxGotFocus;

        if (restoreZeroOnBlur)
            box.LostFocus += OnMoneyBoxLostFocus;
    }

    // A box already showing 0 is cleared on entry, so typing "12" gives "12" rather than
    // "012" — the caret would otherwise land after the existing zero. Leaving the box empty
    // or invalid restores a valid zero on exit.
    private void OnMoneyBoxGotFocus(object sender, RoutedEventArgs e)
    {
        // IsReadOnly is checked as well as IsEnabled: a read-only box (e.g. a tax box while the
        // stage is settled by cash) still takes focus, and clearing its text programmatically
        // would succeed and blank a value the user is not allowed to change.
        if (sender is not TextBox box || !box.IsEnabled || box.IsReadOnly)
            return;

        if (box.Text.Length > 0 && ParseDecimalOrZero(box.Text) == 0m)
        {
            _syncingPayment = true;
            box.Clear();
            _syncingPayment = false;
        }
        box.SelectAll();
    }

    private void OnMoneyBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        if (string.IsNullOrWhiteSpace(box.Text) || !decimal.TryParse(box.Text, out _))
        {
            _syncingPayment = true;
            box.Text = "0";
            _syncingPayment = false;
            // runAutoComplete stays false: restoring a zero must not move a payment method.
            // The deposit boxes' own TextChanged handler already ran the auto-complete pass.
            RefreshComputedTotals(runAutoComplete: false);
        }
    }

    // Static now that the paste handler it attaches is: nothing here touches the window.
    private static void RegisterDecimalTextBox(TextBox textBox)
    {
        DataObject.AddPastingHandler(textBox, OnDecimalTextBoxPaste);
    }

    /// <summary>
    /// Static: attached only through <c>DataObject.AddPastingHandler</c> from code, never named in
    /// XAML. A handler XAML wires up cannot be static, because the generated InitializeComponent
    /// references it as <c>this.Handler</c>.
    /// </summary>
    private static void OnDecimalTextBoxPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (!e.SourceDataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.SourceDataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        var proposedText = GetProposedText(textBox, pastedText);
        if (!DecimalInputPattern.IsMatch(proposedText))
            e.CancelCommand();
    }

    private static string GetProposedText(TextBox textBox, string newText)
    {
        var currentText = textBox.Text ?? string.Empty;
        var selectionStart = textBox.SelectionStart;
        var selectionLength = textBox.SelectionLength;
        return currentText.Remove(selectionStart, selectionLength).Insert(selectionStart, newText);
    }
}
