using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class PaymentsContext : DbContext
{
    public PaymentsContext(DbContextOptions<PaymentsContext> options) : base(options) { }

    public DbSet<ShopperAccount> ShopperAccounts => Set<ShopperAccount>();
    public DbSet<SavedPaymentMethod> SavedPaymentMethods => Set<SavedPaymentMethod>();
    public DbSet<ShopOrder> ShopOrders => Set<ShopOrder>();
    public DbSet<ShopOrderLine> ShopOrderLines => Set<ShopOrderLine>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ShopperAccount>(e => { e.HasKey(x => x.Email); e.Property(x => x.Email).HasMaxLength(256); });
        b.Entity<SavedPaymentMethod>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.ShopperEmail).HasMaxLength(256);
            e.HasIndex(x => x.ShopperEmail);
        });
        b.Entity<ShopOrder>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.BuyerEmail).HasMaxLength(256);
            e.Property(x => x.Subtotal).HasColumnType("decimal(18,2)");
            e.Property(x => x.Fee).HasColumnType("decimal(18,2)");
            e.Property(x => x.Total).HasColumnType("decimal(18,2)");
            e.Property(x => x.RefundAmount).HasColumnType("decimal(18,2)");
            e.Ignore(x => x.IsPaid);
            e.HasIndex(x => x.BuyerEmail);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ShopOrderLine>(e => e.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)"));
    }
}
