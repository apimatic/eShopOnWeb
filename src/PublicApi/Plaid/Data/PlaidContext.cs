using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.PublicApi.Plaid.Data;

public class PlaidContext : DbContext
{
    public PlaidContext(DbContextOptions<PlaidContext> options) : base(options) { }

    public DbSet<BankConnection> Connections => Set<BankConnection>();
    public DbSet<BankAccount> Accounts => Set<BankAccount>();
    public DbSet<BankTransaction> Transactions => Set<BankTransaction>();
    public DbSet<BankPayment> Payments => Set<BankPayment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<BankConnection>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserName).HasMaxLength(256).IsRequired();
            e.Property(x => x.ItemId).HasMaxLength(100).IsRequired();
            e.Property(x => x.ProtectedAccessToken).IsRequired();
            e.Property(x => x.InstitutionId).HasMaxLength(100);
            e.Property(x => x.InstitutionName).HasMaxLength(300);
            e.Property(x => x.Products).HasMaxLength(500);
            e.Property(x => x.TransactionsCursor).HasMaxLength(1000);
            e.HasIndex(x => x.UserName);
            e.HasIndex(x => new { x.UserName, x.ItemId }).IsUnique();
            e.HasMany(x => x.Accounts).WithOne().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BankAccount>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PlaidAccountId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Name).HasMaxLength(300);
            e.Property(x => x.Mask).HasMaxLength(20);
            e.Property(x => x.Type).HasMaxLength(50);
            e.Property(x => x.Subtype).HasMaxLength(50);
        });

        b.Entity<BankTransaction>(e =>
        {
            e.HasKey(x => new { x.ConnectionId, x.TransactionId });
            e.Property(x => x.TransactionId).HasMaxLength(100);
            e.Property(x => x.PlaidAccountId).HasMaxLength(100);
            e.Property(x => x.Name).HasMaxLength(500);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasIndex(x => x.Date);
        });

        b.Entity<BankPayment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserName).HasMaxLength(256).IsRequired();
            e.Property(x => x.PlaidAccountId).HasMaxLength(100);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Speed).HasMaxLength(20);
            e.Property(x => x.ItemsTotal).HasPrecision(18, 2);
            e.Property(x => x.ShippingCost).HasPrecision(18, 2);
            e.Property(x => x.Fee).HasPrecision(18, 2);
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasIndex(x => x.OrderId);
            e.HasIndex(x => x.UserName);
        });
    }
}
