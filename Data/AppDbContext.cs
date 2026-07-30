using Microsoft.EntityFrameworkCore;
using CameywareOrder.Models;
using CameywareOrder.Services;

namespace CameywareOrder.Data;

public class AppDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;
    public DbSet<Shop> Shops { get; set; } = null!;

    /// <summary>
    /// Shop this context is scoped to, captured when it is constructed. An INSTANCE field rather
    /// than a static lookup on purpose: EF parameterises instance-field references in a query
    /// filter, whereas a static would be baked into the compiled query and the first shop opened
    /// would be the only one that ever worked.
    ///
    /// Contexts are scoped and every operation creates a fresh scope, so switching shops is picked
    /// up by the next query with nothing to invalidate. Zero means "no shop open" — only reachable
    /// during startup and at design time — and filters everything out, which fails safe: showing
    /// nothing is recoverable, showing another shop's orders is not.
    /// </summary>
    private readonly int _shopId;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        _shopId = ShopContext.Instance.Current?.Id ?? 0;
    }

    // Both no-argument overloads delegate to these, so overriding the pair covers every save.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampNewOrdersWithShop();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampNewOrdersWithShop();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Assigns the open shop to every newly added order, centrally.
    ///
    /// This is deliberately NOT done at the call sites: several of them (Copy Order, the GraphQL
    /// create mutation) build an Order from an explicit property list, and any one of them that
    /// forgot ShopId would write an order belonging to shop 0 — saved without error, then
    /// invisible in every view. That exact bug was observed during development, which is why the
    /// rule lives in one place that no call site can bypass.
    ///
    /// RequireCurrent throws when no shop is open. Refusing to write is the point: a silent write
    /// to a nonexistent shop loses the order.
    /// </summary>
    private bool _stampingSuppressed;

    /// <summary>
    /// Turns the stamping above off for the lifetime of the returned scope. The ONE legitimate caller
    /// is restoring a shop from an archive.
    /// </summary>
    /// <remarks>
    /// An import already knows which shop each order belongs to, which currency it was priced in and
    /// which pricing mode it was quoted under — those are facts recorded when the order was taken,
    /// possibly on another machine. Stamping would overwrite all three with the shop that happens to be
    /// OPEN, quietly re-parenting every restored order and re-denominating its money. The bug would not
    /// surface until somebody reprinted a receipt.
    ///
    /// Deliberately awkward to reach: an explicit `using` around the save, not a constructor flag or a
    /// setter. The stamping exists precisely so a call site cannot forget `ShopId`, and this is the only
    /// place where the caller is more authoritative than the open shop.
    /// </remarks>
    public IDisposable SuppressShopStamping()
    {
        _stampingSuppressed = true;
        return new StampingSuppression(this);
    }

    private sealed class StampingSuppression : IDisposable
    {
        private readonly AppDbContext _context;

        public StampingSuppression(AppDbContext context) => _context = context;

        public void Dispose() => _context._stampingSuppressed = false;
    }

    private void StampNewOrdersWithShop()
    {
        if (_stampingSuppressed)
            return;

        var addedOrders = ChangeTracker.Entries<Order>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToList();

        if (addedOrders.Count == 0)
            return;

        var shop = ShopContext.Instance.RequireCurrent();

        foreach (var order in addedOrders)
        {
            order.ShopId = shop.Id;
            // Stamp the shop's currency onto the order too. The column has existed unused since
            // currency became a global setting; recording what was actually charged makes a
            // per-order currency history possible later without another migration.
            order.CurrencyType = shop.CurrencyType;
            // Freeze the shop's pricing mode onto the order for the same reason: a receipt reprinted
            // after the shop relocates, or a jurisdiction's rate changes, must still read as it was
            // charged. Derived from the shop's location, not stored on the shop.
            order.PricesIncludeTax = TaxJurisdictions.PricesIncludeTax(shop);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrderNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CustomerName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PhoneNumber).IsRequired().HasMaxLength(30);
            entity.Property(e => e.Email).HasMaxLength(120);
            entity.Property(e => e.Address).HasMaxLength(300);
            entity.Property(e => e.LastModifiedBy).HasMaxLength(120);
            entity.Property(e => e.CurrencyType);
            entity.Property(e => e.PricesIncludeTax);
            entity.Property(e => e.ServiceDetails).HasMaxLength(500);
            entity.Property(e => e.AdditionalNotes).HasMaxLength(1000);
            entity.Property(e => e.Subtotal).HasPrecision(18, 2);
            entity.Property(e => e.TaxRate).HasPrecision(5, 2);
            entity.Property(e => e.ChestSize).HasMaxLength(50);
            entity.Property(e => e.JacketLength).HasMaxLength(50);
            entity.Property(e => e.CustomMadeRecordsJson).HasColumnType("TEXT");
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);
            entity.Property(e => e.Downpayment).HasPrecision(18, 2);
            entity.Property(e => e.AlterationDownpayment).HasPrecision(18, 2);
            entity.Property(e => e.CustomMadeDownpayment).HasPrecision(18, 2);
            entity.Property(e => e.ClothingDownpayment).HasPrecision(18, 2);
            // Scalar only, deliberately: SQLite cannot add a foreign key to an existing table
            // without rebuilding it, and the Shops table is created by a runtime DDL guard rather
            // than a migration. The index is what the shop-filtered order list actually needs.
            entity.HasIndex(e => e.ShopId);

            // Every read of Orders anywhere in the app — the list, search, printing, the GraphQL
            // resolvers — is confined to the open shop by this one line, so a future query cannot
            // leak another shop's data by forgetting a Where clause. IgnoreQueryFilters() is the
            // deliberate escape hatch for a cross-shop view later.
            // NOTE: Find/FindAsync bypass query filters (they are key lookups), so any code path
            // using them must check the shop itself.
            entity.HasQueryFilter(e => e.ShopId == _shopId);

            entity.HasMany(e => e.Items)
                  .WithOne(i => i.Order)
                  .HasForeignKey(i => i.OrderId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Must stay byte-compatible with the CREATE TABLE guard in App.xaml.cs: EF 8 performs no
        // model-vs-database check, so a mismatch surfaces as a runtime materialization failure
        // rather than a build error.
        modelBuilder.Entity<Shop>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PublicId).IsRequired();
            entity.HasIndex(e => e.PublicId).IsUnique();
            entity.Property(e => e.Code).HasMaxLength(20);
            entity.Property(e => e.LocationCode).HasMaxLength(10);
            entity.Property(e => e.NamesJson).IsRequired().HasColumnType("TEXT");
            entity.Property(e => e.PreferredLanguageCode).HasMaxLength(20);
            entity.Property(e => e.PaymentTaxRulesJson).HasColumnType("TEXT");
            entity.Property(e => e.InstalledLanguagesJson).HasColumnType("TEXT");
            entity.Property(e => e.SupportedCurrenciesJson).HasColumnType("TEXT");
            entity.Property(e => e.OrderNumberPrefix).HasMaxLength(20);
            entity.Property(e => e.OrderNumberSequenceKey).HasMaxLength(20);
            entity.Ignore(e => e.Names);                    // computed from NamesJson
            entity.Ignore(e => e.PaymentTaxRules);          // computed from PaymentTaxRulesJson
            entity.Ignore(e => e.InstalledLanguageCodes);   // computed from InstalledLanguagesJson
            entity.Ignore(e => e.SupportedCurrencies);      // computed from SupportedCurrenciesJson
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProductName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
            entity.Property(e => e.PromotionalPrice).HasPrecision(18, 2);
            entity.Ignore(e => e.EffectiveUnitPrice);
            entity.Ignore(e => e.TotalPrice); // computed, not stored
        });
    }
}
