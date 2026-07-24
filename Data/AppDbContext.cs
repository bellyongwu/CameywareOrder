using Microsoft.EntityFrameworkCore;
using LeeYongeOrdering.Models;

namespace LeeYongeOrdering.Data;

public class AppDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

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
            entity.Property(e => e.CurrencyType);
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
            entity.HasMany(e => e.Items)
                  .WithOne(i => i.Order)
                  .HasForeignKey(i => i.OrderId)
                  .OnDelete(DeleteBehavior.Cascade);
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
