using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.Infrastructure.Stax;

public class StaxContext : DbContext
{
    public StaxContext(DbContextOptions<StaxContext> options) : base(options) { }

    public DbSet<StaxCustomerLink> CustomerLinks => Set<StaxCustomerLink>();
    public DbSet<ShopOrder> ShopOrders => Set<ShopOrder>();
    public DbSet<ShopOrderLine> ShopOrderLines => Set<ShopOrderLine>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<StaxCustomerLink>(e =>
        {
            e.ToTable("StaxCustomerLinks");
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.StaxCustomerId).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Email);
            e.HasIndex(x => new { x.Email, x.StaxCustomerId }).IsUnique();
        });
        b.Entity<ShopOrder>(e =>
        {
            e.ToTable("ShopOrders");
            e.Property(x => x.BuyerEmail).HasMaxLength(256).IsRequired();
            e.HasIndex(x => x.BuyerEmail);
            e.Property(x => x.Subtotal).HasColumnType("decimal(18,2)");
            e.Property(x => x.Fee).HasColumnType("decimal(18,2)");
            e.Property(x => x.Total).HasColumnType("decimal(18,2)");
            e.Property(x => x.RefundedAmount).HasColumnType("decimal(18,2)");
            e.Property(x => x.PayState).HasMaxLength(32).IsRequired();
            e.Property(x => x.RefundStatus).HasMaxLength(32).IsRequired();
            e.Property(x => x.PaymentMethodId).HasMaxLength(64);
            e.Property(x => x.PaymentMethodType).HasMaxLength(16);
            e.Property(x => x.StaxTransactionId).HasMaxLength(64);
            e.Property(x => x.RefundTransactionId).HasMaxLength(64);
            e.Property(x => x.PayIdempotencyKey).HasMaxLength(64);
            e.Property(x => x.PaymentMessage).HasMaxLength(500);
            e.Property(x => x.RefundNote).HasMaxLength(500);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ShopOrderId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ShopOrderLine>(e =>
        {
            e.ToTable("ShopOrderLines");
            e.Property(x => x.ProductName).HasMaxLength(100).IsRequired();
            e.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
        });
    }
}
