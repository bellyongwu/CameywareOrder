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
    {
        // Reading the whole customer list over HTTP is at least as serious as reading it on screen,
        // so it answers to the same capability. Until v9.3.0 it answered to nothing at all.
        ApiAuthorization.Require(AppCapability.ViewOrders);

        return context.Orders.Include(o => o.Items);
    }

    /// <summary>
    /// Returns a single order by ID.
    /// Example: { order(id: 1) { id orderNumber items { productName quantity } } }
    /// </summary>
    public static async Task<Order?> GetOrderAsync(int id, AppDbContext context)
    {
        ApiAuthorization.Require(AppCapability.ViewOrders);

        return await context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);
    }
}
