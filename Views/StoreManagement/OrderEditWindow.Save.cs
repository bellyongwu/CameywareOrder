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
    // Saving: the Save click, the insert and update paths, and the field-by-field copy onto the order. Validation is in OrderEditWindow.Validation.cs and is called from here.

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        if (!TryValidateForSave(out var status))
            return;

        var serviceType = GetSelectedServiceType();
        var data = new OrderSaveData(
            status,
            serviceType,
            GetSubtotalForServiceType(serviceType),
            GetTaxRateForServiceType(serviceType),
            // Every section is persisted independently, so clothing items are always captured.
            BuildClothingItems(),
            _customMadeRecords.Count == 0 ? null : JsonSerializer.Serialize(_customMadeRecords));

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            string? newOrderNumber = null;
            if (_existing is null)
                newOrderNumber = AddNewOrder(db, data);
            else
                await UpdateExistingOrderAsync(db, data);

            await db.SaveChangesAsync();

            // Only after the order is safely written: the shop's receipt counter must never move
            // for an order that failed to save, or the run would show a gap nobody can account for.
            if (newOrderNumber is not null)
                AdvanceShopReceiptCounter(newOrderNumber);

            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = _localization.Format("OrderEdit.SaveFailed", ex.Message);
        }
    }

    /// <summary>Adds the new order and returns the number it was given.</summary>
    private string AddNewOrder(AppDbContext db, OrderSaveData data)
    {
        var newOrder = new Order
        {
            OrderNumber = ResolveNewOrderNumber(db),
            OrderDate = DateTime.UtcNow,
            Items = data.ClothingItems
        };
        ApplyEditableFields(newOrder, data);
        // A new order is a change by definition, so it is stamped unconditionally. Only an EDIT can
        // turn out to have altered nothing.
        StampLastModified(newOrder);
        db.Orders.Add(newOrder);

        return newOrder.OrderNumber;
    }

    /// <summary>
    /// The number this order is actually saved under. What the box shows was only a preview, and
    /// the shop may have booked other orders since this window opened, so the number is re-drawn
    /// here — unless the user typed one of their own, which always wins.
    /// </summary>
    private string ResolveNewOrderNumber(AppDbContext db)
    {
        var typed = OrderNumberBox.Text.Trim();
        var shop = ShopContext.Instance.RequireCurrent();

        var stillThePreview = string.Equals(
            typed, OrderNumberFormatter.Preview(shop, DateTime.Now), StringComparison.Ordinal);

        return stillThePreview ? OrderNumberFormatter.Reserve(db, shop, DateTime.Now) : typed;
    }

    // Moves the shop's running number past the one just used, and persists it.
    private static void AdvanceShopReceiptCounter(string orderNumber)
        => ShopContext.Instance.UpdateActiveShop(
            shop => OrderNumberFormatter.CommitSequence(shop, orderNumber, DateTime.Now));

    private async Task UpdateExistingOrderAsync(AppDbContext db, OrderSaveData data)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == _existing!.Id);

        if (order is null)
            return;

        ApplyEditableFields(order, data);

        // The items are replaced only when they actually differ. The old code removed and re-added
        // them every time, which made the change check below answer "changed" for every save — the
        // one thing it must not do.
        var itemsChanged = !ClothingItemsMatch(order.Items, data.ClothingItems);
        if (itemsChanged)
        {
            db.OrderItems.RemoveRange(order.Items);
            order.Items.Clear();
            foreach (var clothingItem in data.ClothingItems)
                order.Items.Add(clothingItem);
        }

        // Ask EF whether anything actually moved, rather than comparing the form to a snapshot taken
        // when the window opened. EF holds the values the row was LOADED with and compares column by
        // column, so this covers every mapped field — including the JSON blobs the form does not
        // model as fields — and it keeps covering a column added next year without anyone
        // remembering to extend a list. Reading Entry() runs change detection.
        if (itemsChanged || db.Entry(order).Properties.Any(property => property.IsModified))
            StampLastModified(order);
    }

    /// <summary>
    /// Whether the clothing lines on the form are the ones already stored, line for line.
    /// </summary>
    /// <remarks>
    /// Position matters, so this is a pairwise walk rather than a set comparison: the list is the
    /// order the shop typed and reordering it is an edit a reader would notice on the receipt.
    /// Existing rows are taken in <c>Id</c> order because that is the order they were inserted in,
    /// which is the row order that was on screen when they were saved.
    /// </remarks>
    private static bool ClothingItemsMatch(ICollection<OrderItem> stored, IReadOnlyList<OrderItem> onForm)
    {
        if (stored.Count != onForm.Count)
            return false;

        var ordered = stored.OrderBy(item => item.Id).ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            if (!string.Equals(ordered[i].ProductName, onForm[i].ProductName, StringComparison.Ordinal)
                || ordered[i].Quantity != onForm[i].Quantity
                || ordered[i].UnitPrice != onForm[i].UnitPrice
                || ordered[i].PromotionalPrice != onForm[i].PromotionalPrice)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Records who saved this order and when.</summary>
    /// <remarks>
    /// Called only where something actually changed. Opening an order, looking at it and pressing
    /// Save is not a modification, and stamping it as one overwrites a real record of who last
    /// touched the order with the name of whoever last read it.
    ///
    /// The name comes from the session rather than from anything on the form — "who saved this" is
    /// not a field anybody should be able to type. Left untouched when nobody is signed in (only
    /// reachable from a harness), so a save can never blank a name a real crew member left behind.
    /// </remarks>
    private static void StampLastModified(Order order)
    {
        order.LastModifiedDate = DateTime.UtcNow;

        if (AuthenticationService.Instance.CurrentUser is { } crew)
            order.LastModifiedBy = crew.DisplayLabel;
    }

    private void ApplyEditableFields(Order order, OrderSaveData data)
    {
        // Reads the order's OWN date as the baseline, which is what makes one line serve both paths:
        // a new order is seeded with UtcNow just above, an existing one holds what was stored. Either
        // way an untouched picker returns it unchanged — see Order.ResolveOrderDate.
        order.OrderDate = Order.ResolveOrderDate(OrderDatePicker.SelectedDate, order.OrderDate);
        // Straight through, unlike the order date: this one has no live default to preserve, so the
        // day on the picker IS the answer. Validation has already refused a blank one.
        order.ExpectedPickupDate = PickupDatePicker.SelectedDate is { } pickup
            ? Order.ToStoredDate(pickup)
            : null;
        order.CustomerName = CustomerNameBox.Text.Trim();
        // Stored with its dial code in front, in the same column it always used: "+1 905-401-6667".
        order.PhoneNumber = PhoneField.FullNumber;
        ApplyPaymentSplits(order);
        order.Email = string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim();
        order.Address = string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim();
        ApplyStatusReasonFields(order, data.Status);
        order.ServiceType = data.ServiceType;
        order.ServiceDetails = (AlterationCategoryBox.SelectedItem as ComboBoxItem)?.Tag as string;
        order.AdditionalNotes = NullIfWhiteSpace(AlterationAdditionalNotesBox.Text);
        order.Subtotal = data.Subtotal;
        order.TaxRate = data.TaxRate;
        order.ChestSize = null;
        order.JacketLength = null;
        order.CustomMadeRecordsJson = data.CustomMadeJson;
        order.Status = data.Status;
        order.TotalAmount = _totalAmount;
        // The order records the money it was priced in. This line is the reason the column exists.
        // Until it was added nothing wrote the column, so every saved order carried the enum default
        // regardless of what its shop actually traded in.
        order.CurrencyType = SelectedCurrency;
        order.Notes = NullIfWhiteSpace(NotesBox.Text);
        // The audit stamp is deliberately NOT written here. This method assigns whatever the form
        // holds, which is how the change check works — every assignment of an unchanged value leaves
        // EF's IsModified false. Writing a fresh timestamp in here would make every save look like a
        // change, including the ones that are not. See StampLastModified.
        ApplyPaymentFields(order);
    }

    // Persists the preset category (only meaningful for cancelled/returned orders) and the
    // free-text detail (only meaningful when that category is "Other"); both are cleared
    // once the order is no longer cancelled/returned.
    private void ApplyStatusReasonFields(Order order, OrderStatus status)
    {
        if (status is not (OrderStatus.Cancelled or OrderStatus.Returned))
        {
            order.StatusReasonCategory = null;
            order.StatusReason = null;
            return;
        }

        var category = (StatusReasonCategoryBox.SelectedItem as ComboBoxItem)?.Tag as string;
        order.StatusReasonCategory = category;
        order.StatusReason = category == OtherStatusReasonTag ? NullIfWhiteSpace(StatusReasonBox.Text) : null;
    }
}
