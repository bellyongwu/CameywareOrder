using Microsoft.EntityFrameworkCore;
using LeeYongeOrdering.Data;
using LeeYongeOrdering.Models;

namespace LeeYongeOrdering.GraphQL;

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
        var order = await context.Orders.FindAsync(input.Id);
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
    /// Deletes an order and all its items (cascade).
    /// </summary>
    public static async Task<bool> DeleteOrderAsync(int id, AppDbContext context)
    {
        var order = await context.Orders.FindAsync(id);
        if (order is null) return false;

        context.Orders.Remove(order);
        await context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Adds a line item to an existing order and recalculates the order total.
    /// </summary>
    public async Task<OrderItem> AddOrderItemAsync(AddOrderItemInput input, AppDbContext context)
    {
        var item = new OrderItem
        {
            OrderId = input.OrderId,
            ProductName = input.ProductName,
            Quantity = input.Quantity,
            UnitPrice = input.UnitPrice
        };
        context.OrderItems.Add(item);

        // Recalculate order total
        var order = await context.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == input.OrderId);
        if (order is not null)
            order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPrice) + input.Quantity * input.UnitPrice;

        await context.SaveChangesAsync();
        return item;
    }

    /// <summary>
    /// Removes a line item and recalculates the order total.
    /// </summary>
    public static async Task<bool> RemoveOrderItemAsync(int itemId, AppDbContext context)
    {
        var item = await context.OrderItems.FindAsync(itemId);
        if (item is null) return false;

        context.OrderItems.Remove(item);

        var order = await context.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == item.OrderId);
        if (order is not null)
            order.TotalAmount = order.Items.Where(i => i.Id != itemId).Sum(i => i.Quantity * i.UnitPrice);

        await context.SaveChangesAsync();
        return true;
    }
}
