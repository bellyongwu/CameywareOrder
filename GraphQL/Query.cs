using HotChocolate.Data;
using Microsoft.EntityFrameworkCore;
using CameywareOrder.Data;
using CameywareOrder.Models;

namespace CameywareOrder.GraphQL;

public class Query
{
    /// <summary>
    /// Returns all orders. Supports filtering and sorting via Hot Chocolate.
    /// Example: { orders(where: { status: { eq: PROCESSING } }) { id orderNumber customerName } }
    /// </summary>
    [UseFiltering]
    [UseSorting]
    public IQueryable<Order> GetOrders(AppDbContext context)
        => context.Orders.Include(o => o.Items);

    /// <summary>
    /// Returns a single order by ID.
    /// Example: { order(id: 1) { id orderNumber items { productName quantity } } }
    /// </summary>
    public static async Task<Order?> GetOrderAsync(int id, AppDbContext context)
        => await context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);
}
