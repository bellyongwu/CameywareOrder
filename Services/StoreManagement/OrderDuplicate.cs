using System.Reflection;
using CameywareOrder.Data;
using CameywareOrder.Models;
using Microsoft.EntityFrameworkCore;

namespace CameywareOrder.Services;

/// <summary>
/// What a copied order inherits from the one it was made from.
/// </summary>
/// <remarks>
/// **Projected from the model, not written out by hand.** This was a 43-line property list, and the
/// list is exactly as complete as whoever last added a column remembered to make it. Four had been
/// missed by v9.2.1 — `PricesIncludeTax`, the three per-stage `XxxFinalTaxRate` columns and
/// `PaymentSplitsJson` — and the failure was silent and expensive: a copy of a 1,000.00 tax-INCLUSIVE
/// order stored 1,000.00 and recomputed 1,060.00, because losing the pricing mode switches the copy
/// to the other arithmetic while it keeps the source's stored total. Nothing failed to build and
/// nothing went red.
///
/// So the default is inverted. Every mapped scalar EF knows about travels, and the columns that must
/// NOT travel are named in <see cref="NotInherited"/> — one list, each entry with a reason. A column
/// added next year is inherited without anybody doing anything, which is the correct default for a
/// duplicate; a column that genuinely must not travel fails loudly at review rather than quietly at
/// the till.
///
/// Navigations are not scalars and are therefore untouched by the projection —
/// <see cref="Build"/> deep-copies <see cref="Order.Items"/> explicitly, by the same rule.
/// </remarks>
public static class OrderDuplicate
{
    /// <summary>
    /// The columns a copy deliberately does not inherit. The ONE place that decision is recorded.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>Id</c> / <c>ShopId</c> — a new row in the open shop; the context stamps the shop.</item>
    /// <item><c>OrderNumber</c> — drawn from the shop's own receipt run, like any new order.</item>
    /// <item><c>CustomerName</c> — carries the copy suffix; see <see cref="OrderCopyName"/>.</item>
    /// <item><c>OrderDate</c> / <c>LastModifiedDate</c> / <c>LastModifiedBy</c> — the copy is written
    ///   now, by whoever is signed in. Inheriting these would date a new order to last April and
    ///   credit it to somebody who never saw it.</item>
    /// <item><c>Status</c> — a FINISHED order copied becomes an active one again; handled in
    ///   <see cref="Build"/> rather than simply dropped, since an unfinished status does travel.</item>
    /// <item><c>StatusReason</c> / <c>StatusReasonCategory</c> — why the SOURCE was cancelled says
    ///   nothing about the copy, and would otherwise sit on an active order explaining a refund that
    ///   never happened.</item>
    /// <item><c>ExpectedPickupDate</c> — a promise made to a customer about the source job. The copy
    ///   needs its own, and the order form requires one before it will save.</item>
    /// <item><c>DeletedOnUtc</c> — copying an order out of the recycle bin produces a live order.</item>
    /// <item><c>CurrencyType</c> / <c>PricesIncludeTax</c> — STAMPED, not inherited.
    ///   <c>AppDbContext.StampNewOrdersWithShop</c> writes both onto every added order from the open
    ///   shop, so whatever a copy carried would be overwritten on the way to disk. Listing them here
    ///   records that this is understood rather than overlooked: a new order in this shop is priced
    ///   the way this shop prices, and copying a legacy order therefore RE-PRICES it in today's
    ///   mode — which is the stamp's deliberate reading, not a copy defect.</item>
    /// </list>
    /// </remarks>
    public static readonly IReadOnlyList<string> NotInherited = new[]
    {
        nameof(Order.Id),
        nameof(Order.ShopId),
        nameof(Order.CurrencyType),
        nameof(Order.PricesIncludeTax),
        nameof(Order.OrderNumber),
        nameof(Order.CustomerName),
        nameof(Order.OrderDate),
        nameof(Order.LastModifiedDate),
        nameof(Order.LastModifiedBy),
        nameof(Order.Status),
        nameof(Order.StatusReason),
        nameof(Order.StatusReasonCategory),
        nameof(Order.ExpectedPickupDate),
        nameof(Order.DeletedOnUtc),
    };

    /// <summary>Line-item columns a copied line does not inherit — its identity and its parent.</summary>
    public static readonly IReadOnlyList<string> ItemNotInherited = new[]
    {
        nameof(OrderItem.Id),
        nameof(OrderItem.OrderId),
    };

    /// <summary>
    /// Statuses that represent a finished order. Copying one starts a fresh ACTIVE order, so its
    /// status resets — which also clears the "picked up" tick, that flag having no column of its own.
    /// </summary>
    public static bool IsClosedStatus(OrderStatus status)
        => status is OrderStatus.Shipped or OrderStatus.Completed
            or OrderStatus.Cancelled or OrderStatus.Returned;

    /// <summary>
    /// Builds the copy. <paramref name="orderNumber"/> and <paramref name="customerName"/> are the
    /// caller's to reserve, because both are drawn from state a single order cannot see — the shop's
    /// receipt run and every other customer name in the shop.
    /// </summary>
    public static Order Build(
        AppDbContext db, Order source, string orderNumber, string customerName, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(source);

        var copy = new Order();
        CopyScalars(db, source, copy, NotInherited);

        copy.OrderNumber = orderNumber;
        copy.CustomerName = customerName;
        copy.OrderDate = nowUtc;
        copy.LastModifiedDate = nowUtc;
        copy.Status = IsClosedStatus(source.Status) ? OrderStatus.Processing : source.Status;

        // The same rule the order form applies: who wrote this comes from the session, never from a
        // field. Left null when nobody is signed in, which only a harness reaches.
        if (AuthenticationService.Instance.CurrentUser is { } crew)
            copy.LastModifiedBy = crew.DisplayLabel;

        copy.Items = source.Items.Select(item => CopyItem(db, item)).ToList();

        return copy;
    }

    private static OrderItem CopyItem(AppDbContext db, OrderItem source)
    {
        var copy = new OrderItem();
        CopyScalars(db, source, copy, ItemNotInherited);
        return copy;
    }

    /// <summary>
    /// Every mapped scalar EF knows about, less the named exclusions.
    /// </summary>
    /// <remarks>
    /// Driven from <c>db.Model</c> rather than from <c>typeof(T).GetProperties()</c> on purpose: the
    /// model is what the DATABASE has, so a computed <c>[NotMapped]</c> member is excluded by
    /// construction and cannot be "copied" into a property with no setter. Shadow properties carry no
    /// <c>PropertyInfo</c> and are skipped — there are none on these two types, and if one is ever
    /// added, silently skipping it is the safe reading for a duplicate.
    /// </remarks>
    private static void CopyScalars<T>(AppDbContext db, T source, T target, IReadOnlyList<string> exclude)
    {
        var entityType = db.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"{typeof(T).Name} is not part of the EF model.");

        foreach (var property in entityType.GetProperties())
        {
            var member = property.PropertyInfo;
            if (member is null || !member.CanWrite || exclude.Contains(property.Name))
                continue;

            member.SetValue(target, member.GetValue(source));
        }
    }

    /// <summary>
    /// Every mapped scalar that IS inherited, for a harness to compare source against copy without
    /// re-deriving the rule it is checking.
    /// </summary>
    public static IReadOnlyList<PropertyInfo> InheritedProperties(AppDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        return db.Model.FindEntityType(typeof(Order))!
            .GetProperties()
            .Select(property => property.PropertyInfo)
            .Where(member => member is not null && member.CanWrite && !NotInherited.Contains(member.Name))
            .Select(member => member!)
            .ToList();
    }
}
