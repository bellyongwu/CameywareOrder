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
    // The custom-made measurement records this order owns: the list, and adding, editing and removing one. The measurements themselves and their attached images belong to CustomMadeServiceWindow.

    // The record button opens the custom-made editor in view mode when the whole
    // order is read-only OR the custom-made section balance is cleared (settled),
    // so its label mirrors that state (View vs. Edit).
    private void RefreshCustomMadeButtonLabel()
    {
        var viewOnly = _isReadOnly || IsSettled(_customMadeControls);
        EditCustomMadeButton.Content = _localization[viewOnly ? "OrderEdit.ViewCustomMade" : "OrderEdit.EditCustomMade"];
    }

    private void InitializeCustomMadeRecordsList()
    {
        CustomMadeRecordsList.ItemsSource = _customMadeRecords;
    }

    private void LoadCustomMadeRecords(string? json)
    {
        _customMadeRecords.Clear();

        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var records = JsonSerializer.Deserialize<List<CustomMadeServiceRecord>>(json) ?? new List<CustomMadeServiceRecord>();
                foreach (var record in records)
                    _customMadeRecords.Add(record);
            }
            catch
            {
                // Ignore malformed legacy payloads and start with an empty list.
            }
        }

        RefreshCustomMadeEmptyState();
    }

    private void RefreshCustomMadeEmptyState()
    {
        CustomMadeEmptyText.Visibility = _customMadeRecords.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAddCustomMadeRecordClick(object sender, RoutedEventArgs e)
    {
        if (_isReadOnly)
            return;

        if (!CanOpenCustomMadeWindow())
            return;

        var dialog = new CustomMadeServiceWindow(
            _localization,
            defaultOrderNumber: OrderNumberBox.Text,
            defaultCustomerName: CustomerNameBox.Text,
            defaultPhoneNumber: PhoneField.FullNumber,
            defaultEmail: EmailBox.Text,
            isReadOnly: false)
        {
            Owner = this
        };

        if (dialog.ShowDialog() is true && dialog.Result is not null)
        {
            _customMadeRecords.Add(dialog.Result);
            RefreshCustomMadeEmptyState();
            RefreshComputedTotals();
        }
    }

    private void OnEditCustomMadeRecordClick(object sender, RoutedEventArgs e)
    {
        if (CustomMadeRecordsList.SelectedItem is not CustomMadeServiceRecord selected)
            return;

        // A settled custom-made section (final balance cleared) is locked: the record
        // opens in view mode (title from the OrderEdit.ViewCustomMade key) with every
        // field — including the document upload area — read-only, mirroring the
        // whole-order read-only path.
        var recordReadOnly = _isReadOnly || IsSettled(_customMadeControls);

        if (!recordReadOnly && !CanOpenCustomMadeWindow())
            return;

        var dialog = new CustomMadeServiceWindow(
            _localization,
            existing: selected,
            defaultOrderNumber: OrderNumberBox.Text,
            defaultCustomerName: CustomerNameBox.Text,
            defaultPhoneNumber: PhoneField.FullNumber,
            defaultEmail: EmailBox.Text,
            isReadOnly: recordReadOnly)
        {
            Owner = this
        };

        if (recordReadOnly)
        {
            dialog.ShowDialog();
            return;
        }

        if (dialog.ShowDialog() is true && dialog.Result is not null)
        {
            var index = _customMadeRecords.IndexOf(selected);
            if (index >= 0)
                _customMadeRecords[index] = dialog.Result;
            RefreshCustomMadeEmptyState();
            RefreshComputedTotals();
        }
    }

    private void OnRemoveCustomMadeRecordClick(object sender, RoutedEventArgs e)
    {
        if (_isReadOnly)
            return;

        if (CustomMadeRecordsList.SelectedItem is not CustomMadeServiceRecord selected)
            return;

        _customMadeRecords.Remove(selected);
        RefreshCustomMadeEmptyState();
        RefreshComputedTotals();
    }

    private void OnCustomMadeRecordsDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CustomMadeRecordsList.SelectedItem is not CustomMadeServiceRecord)
            return;

        OnEditCustomMadeRecordClick(sender, new RoutedEventArgs());
    }

    // Requirement 4a: pressing Enter on a selected record opens the same editor
    // dialog as a double-click.
    private void OnCustomMadeRecordsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (CustomMadeRecordsList.SelectedItem is not CustomMadeServiceRecord)
            return;

        e.Handled = true;
        OnEditCustomMadeRecordClick(sender, new RoutedEventArgs());
    }

    /// <summary>
    /// The custom-made editor needs a customer to attach its record to. Routed through the same guard
    /// as Save, so being stopped here marks the same fields in the same places rather than raising a
    /// dialog and leaving the form looking untouched.
    /// </summary>
    private bool CanOpenCustomMadeWindow()
    {
        ClearValidationErrors();
        if (TryRequireFilled(CustomerContactFields()))
            return true;

        AnnounceValidationFailure();
        return false;
    }
}
