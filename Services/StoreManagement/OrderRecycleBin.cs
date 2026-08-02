using System.Diagnostics;
using System.IO;
using CameywareOrder.Data;
using CameywareOrder.Models;
using Microsoft.EntityFrameworkCore;

namespace CameywareOrder.Services;

/// <summary>
/// The recycle bin: deleting an order marks it, restoring clears the mark, and a purge removes what
/// has been in there longer than the installation allows.
/// </summary>
/// <remarks>
/// Deleting used to be immediate and final. In a shop where several people use the same list every
/// day that is one mis-click away from losing a record nobody can reconstruct — the customer's
/// measurements, what they paid, what they are owed. Now <see cref="Order.DeletedOnUtc"/> is stamped
/// instead, `AppDbContext`'s query filter hides the row from every ordinary read, and it can be put
/// back for as long as <see cref="DataProtectionSettings.RecycleBinDays"/> says.
///
/// Every method here reaches rows the query filter hides, so every one says
/// <c>IgnoreQueryFilters()</c> and then restates by hand whichever half of the filter it still
/// meant — the same discipline `ShopAdministration` documents for the cross-shop half. Getting that
/// backwards is how a "restore" would silently reach into another branch.
///
/// This is the ONE place an order is deleted from. The list, the row menu, the Delete key and the
/// GraphQL mutation all route through it, so there is no path that still destroys a record outright.
/// </remarks>
public static class OrderRecycleBin
{
    /// <summary>
    /// Sends orders to the bin and returns how many moved.
    /// </summary>
    /// <remarks>
    /// Takes the ids rather than the entities because every caller has ids and the entities they
    /// hold are `AsNoTracking` copies from the list — writing through one of those saves nothing at
    /// all, silently, which is the worst way for a delete to fail.
    ///
    /// Already-binned rows are skipped rather than re-stamped: re-stamping would restart their
    /// retention window, so deleting a selection twice would quietly keep resurrecting the clock on
    /// records the shop meant to be rid of.
    /// </remarks>
    public static int Delete(AppDbContext db, IReadOnlyList<int> orderIds, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(orderIds);

        if (orderIds.Count == 0)
            return 0;

        // The shop half of the filter still applies — these are the open shop's orders — so this one
        // query needs no escape hatch at all. It is the only method here that does not.
        var orders = db.Orders.Where(order => orderIds.Contains(order.Id)).ToList();
        if (orders.Count == 0)
            return 0;

        foreach (var order in orders)
            order.DeletedOnUtc = nowUtc;

        db.SaveChanges();
        return orders.Count;
    }

    /// <summary>Puts orders back into the live list. Returns how many were restored.</summary>
    public static int Restore(AppDbContext db, IReadOnlyList<int> orderIds)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(orderIds);

        if (orderIds.Count == 0)
            return 0;

        var shopId = ShopContext.Instance.RequireCurrent().Id;

        var orders = db.Orders.IgnoreQueryFilters()
            .Where(order => order.ShopId == shopId
                            && order.DeletedOnUtc != null
                            && orderIds.Contains(order.Id))
            .ToList();

        foreach (var order in orders)
            order.DeletedOnUtc = null;

        db.SaveChanges();
        return orders.Count;
    }

    /// <summary>Everything in the open shop's bin, most recently deleted first.</summary>
    public static List<Order> List(AppDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        var shopId = ShopContext.Instance.RequireCurrent().Id;

        return db.Orders.AsNoTracking().IgnoreQueryFilters()
            .Include(order => order.Items)
            .Where(order => order.ShopId == shopId && order.DeletedOnUtc != null)
            .OrderByDescending(order => order.DeletedOnUtc)
            .ToList();
    }

    /// <summary>How many orders the open shop has in its bin — the badge on the menu entry.</summary>
    public static int Count(AppDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        var shopId = ShopContext.Instance.RequireCurrent().Id;

        return db.Orders.IgnoreQueryFilters()
            .Count(order => order.ShopId == shopId && order.DeletedOnUtc != null);
    }

    /// <summary>
    /// Removes binned orders for good — the ones the caller names, or everything past its retention
    /// window. Returns how many rows went.
    /// </summary>
    /// <remarks>
    /// <paramref name="orderIds"/> null means "whatever the cutoff catches", which is the startup
    /// purge; a list means "these", which is Empty bin and Delete forever. One method for both,
    /// because the destructive half — items first, then the order, then its images, in one
    /// SaveChanges — must not exist twice.
    ///
    /// NOT scoped to a shop, and deliberately so for the purge: retention is an installation setting
    /// and the purge runs at startup, before any shop is open. A shop-scoped purge would leave every
    /// branch but the one somebody happened to open accumulating deleted rows forever.
    /// </remarks>
    public static int PurgeForever(AppDbContext db, DateTime? deletedBeforeUtc, IReadOnlyList<int>? orderIds)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (deletedBeforeUtc is null && orderIds is null)
            return 0;

        var query = db.Orders.IgnoreQueryFilters()
            .Include(order => order.Items)
            .Where(order => order.DeletedOnUtc != null);

        if (deletedBeforeUtc is { } cutoff)
            query = query.Where(order => order.DeletedOnUtc < cutoff);

        if (orderIds is not null)
            query = query.Where(order => orderIds.Contains(order.Id));

        var orders = query.ToList();
        if (orders.Count == 0)
            return 0;

        foreach (var order in orders)
            db.OrderItems.RemoveRange(order.Items);

        db.Orders.RemoveRange(orders);

        // One SaveChanges, so a failure part way leaves nothing half destroyed — the same shape
        // ShopAdministration.Delete uses.
        db.SaveChanges();

        // AFTER the rows are committed, never before: an image deleted for a row that then failed to
        // delete would leave a live order pointing at a file that is gone.
        foreach (var order in orders)
            DeleteAttachedImages(order);

        return orders.Count;
    }

    /// <summary>
    /// Runs the retention purge across every shop. Called once at startup; reports what it removed.
    /// </summary>
    public static int PurgeExpired(AppDbContext db, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(db);

        var cutoff = DataProtectionStore.Instance.Settings.PurgeBefore(nowUtc);
        if (cutoff is null)
            return 0;

        return PurgeForever(db, cutoff, orderIds: null);
    }

    /// <summary>
    /// The images attached to a purged order's measurement records.
    /// </summary>
    /// <remarks>
    /// The image bytes live on disk under <c>Documents/CustomMade</c> and only a reference is stored
    /// on the order, so removing the row alone leaks the files — permanently, since nothing else
    /// knows their names once the record that listed them is gone. Best-effort, and never allowed to
    /// throw: the rows are already committed by the time this runs, and a locked file must not turn a
    /// completed purge into an error.
    /// </remarks>
    private static void DeleteAttachedImages(Order order)
    {
        try
        {
            foreach (var document in order.CustomMadeRecords.SelectMany(record => record.Documents))
                DocumentStorageService.Delete(document);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"[recyclebin] could not remove images for {order.OrderNumber}: {ex.Message}");
        }
    }
}
