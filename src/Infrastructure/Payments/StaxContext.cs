using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class StaxContext : DbContext
{
    public StaxContext(DbContextOptions<StaxContext> options) : base(options) { }

    public DbSet<ShopperAccount> ShopperAccounts => Set<ShopperAccount>();
    public DbSet<ClosedShopper> ClosedShoppers => Set<ClosedShopper>();
    public DbSet<SavedPaymentMethod> SavedPaymentMethods => Set<SavedPaymentMethod>();
    public DbSet<ShopOrder> ShopOrders => Set<ShopOrder>();
    public DbSet<ShopOrderItem> ShopOrderItems => Set<ShopOrderItem>();
    public DbSet<InvoiceScheduleRecord> InvoiceSchedules => Set<InvoiceScheduleRecord>();
    public DbSet<SellerRecord> Sellers => Set<SellerRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ShopperAccount>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.StaxCustomerId).HasMaxLength(64);
        });
        b.Entity<ClosedShopper>(e =>
        {
            e.HasIndex(x => x.EmailHash).IsUnique();
            e.Property(x => x.EmailHash).HasMaxLength(64);
        });
        b.Entity<SavedPaymentMethod>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.ShopperEmail).HasMaxLength(256);
            e.HasIndex(x => x.ShopperEmail);
        });
        b.Entity<ShopOrder>(e =>
        {
            e.Property(x => x.ShopperEmail).HasMaxLength(256);
            e.HasIndex(x => x.ShopperEmail);
            e.Property(x => x.Subtotal).HasColumnType("decimal(18,2)");
            e.Property(x => x.Fee).HasColumnType("decimal(18,2)");
            e.Property(x => x.Total).HasColumnType("decimal(18,2)");
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.RefundStatus).HasMaxLength(32);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ShopOrderId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ShopOrderItem>(e => e.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)"));
        b.Entity<InvoiceScheduleRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        });
    }
}
