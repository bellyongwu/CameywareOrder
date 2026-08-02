using Microsoft.EntityFrameworkCore;
using CameywareOrder.Data;
using CameywareOrder.Models;
using CameywareOrder.Services;

namespace CameywareOrder.GraphQL;

// ── Input types ────────────────────────────────────────────────────────────────

public record CreateOrderInput(
    string OrderNumber,
    string CustomerName,
    string PhoneNumber,
    string? Email,
    string? Address,
    OrderStatus Status,
    decimal TotalAmount,
    string? Notes
);

public record UpdateOrderInput(
    int Id,
    string? CustomerName,
    string? PhoneNumber,
    string? Email,
    string? Address,
    OrderStatus? Status,
    decimal? TotalAmount,
    string? Notes
);

public record AddOrderItemInput(
    int OrderId,
    string ProductName,
    int Quantity,
    decimal UnitPrice
);

// ── Mutation type ──────────────────────────────────────────────────────────────

// Every resolver below reaches orders through a LINQ query, never Find/FindAsync. Find is a
// primary-key lookup and bypasses EF query filters, so it would happily return — and then let a
// caller mutate or delete — an order belonging to a different shop. Going through a query means
// the shop filter in AppDbContext applies automatically and these resolvers hold no shop logic of
// their own to drift out of step.
public class Mutation
{
    /// <summary>
    /// Creates a new order.
    /// mutation { createOrder(input: { orderNumber: "ORD-001", customerName: "Alice", phoneNumber: "13800000000", status: PROCESSING, totalAmount: 99.9 }) { id } }
    /// </summary>
    public async Task<Order> CreateOrderAsync(CreateOrderInput input, AppDbContext context)
    {
        if (string.IsNullOrWhiteSpace(input.PhoneNumber))
            throw new ArgumentException("Phone number is required.", nameof(input));

        var order = new Order
        {
            OrderNumber = input.OrderNumber,
            CustomerName = input.CustomerName,
            PhoneNumber = input.PhoneNumber,
            Email = input.Email,
            Address = input.Address,
            OrderDate = DateTime.UtcNow,
            LastModifiedDate = DateTime.UtcNow,
            Status = input.Status,
            TotalAmount = input.TotalAmount,
            Notes = input.Notes
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order;
    }

    /// <summary>
    /// Updates an existing order's mutable fields.
    /// </summary>
    public static async Task<Order?> UpdateOrderAsync(UpdateOrderInput input, AppDbContext context)
    {
        var order = await context.Orders.FirstOrDefaultAsync(o => o.Id == input.Id);
        if (order is null) return null;

        if (input.CustomerName is not null) order.CustomerName = input.CustomerName;
        if (input.PhoneNumber is not null) order.PhoneNumber = input.PhoneNumber;
        if (input.Email is not null)        order.Email = input.Email;
        if (input.Address is not null)      order.Address = input.Address;
        if (input.Status.HasValue)         order.Status = input.Status.Value;
        if (input.TotalAmount.HasValue)    order.TotalAmount = input.TotalAmount.Value;
        if (input.Notes is not null)       order.Notes = input.Notes;

        await context.SaveChangesAsync();
        return order;
    }

    /// <summary>
    /// Sends an order to the recycle bin, where it stays recoverable for the installation's
    /// retention window.
    /// </summary>
    /// <remarks>
    /// Through <c>OrderRecycleBin</c>, the same path the order list uses, so there is no way in to
    /// this application that still destroys a record outright — least of all an unattended API
    /// caller, which is the one that would do it a thousand times before anybody noticed.
    ///
    /// The name is unchanged: to a caller this still means "delete this order", and it still stops
    /// appearing in every query. What changed is only how long the row survives behind that.
    /// </remarks>
    public static bool DeleteOrder(int id, AppDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return OrderRecycleBin.Delete(context, new[] { id }, DateTime.UtcNow) > 0;
    }

    /// <summary>
    /// Adds a line item to an existing order and recalculates the order total.
    /// </summary>
    public async Task<OrderItem> AddOrderItemAsync(AddOrderItemInput input, AppDbContext context)
    {
        // Resolve the parent order FIRST. The lookup is shop-filtered, so an order belonging to
        // another shop comes back null and the item is never created. Previously the item was
        // added before this check and saved regardless of whether the order was found, which let a
        // line item be attached to an order the caller could not otherwise see.
        var order = await context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == input.OrderId)
            ?? throw new ArgumentException("Order not found.", nameof(input));

        var item = new OrderItem
        {
            OrderId = input.OrderId,
            ProductName = input.ProductName,
            Quantity = input.Quantity,
            UnitPrice = input.UnitPrice
        };
        context.OrderItems.Add(item);

        // Recalculate order total
        order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPrice) + input.Quantity * input.UnitPrice;

        await context.SaveChangesAsync();
        return item;
    }

    /// <summary>
    /// Removes a line item and recalculates the order total.
    /// </summary>
    public static async Task<bool> RemoveOrderItemAsync(int itemId, AppDbContext context)
    {
        // Reached through Orders rather than OrderItems: OrderItem carries no shop of its own, and
        // OrderItems.FindAsync would bypass the filter anyway, so a caller could have deleted a
        // line item from another shop's order. Coming in via the (filtered) order also yields the
        // order needed to recalculate the total, in one query.
        var order = await context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Items.Any(i => i.Id == itemId));

        var item = order?.Items.Find(i => i.Id == itemId);
        if (order is null || item is null)
            return false;

        context.OrderItems.Remove(item);
        order.TotalAmount = order.Items.Where(i => i.Id != itemId).Sum(i => i.Quantity * i.UnitPrice);

        await context.SaveChangesAsync();
        return true;
    }
}
