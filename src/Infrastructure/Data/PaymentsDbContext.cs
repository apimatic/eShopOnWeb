using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class PaymentsDbContext : DbContext
{
    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : base(options)
    {
    }

    public DbSet<OrderPayment> OrderPayments => Set<OrderPayment>();
    public DbSet<SavedCard> SavedCards => Set<SavedCard>();
    public DbSet<ShopperProfile> ShopperProfiles => Set<ShopperProfile>();
    public DbSet<RefundRecord> RefundRecords => Set<RefundRecord>();
    public DbSet<PaymentOperationClaim> PaymentOperationClaims => Set<PaymentOperationClaim>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OrderPayment>(builder =>
        {
            builder.HasKey(p => p.OrderId);
            builder.Property(p => p.OrderId).ValueGeneratedNever();
            builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
            builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
            builder.Property(p => p.PaymentAttemptKey).IsRequired().HasMaxLength(64);
            builder.Property(p => p.PaymentMethodId).HasMaxLength(64);
            builder.Property(p => p.OneOffInstrumentId).HasMaxLength(64);
            builder.Property(p => p.AuthorizationId).HasMaxLength(64);
            builder.Property(p => p.AuthorizationStatus).HasMaxLength(32);
            builder.Property(p => p.CaptureTransferId).HasMaxLength(64);
            builder.Property(p => p.TransferState).HasMaxLength(32);
            builder.Property(p => p.LastFailureCode).HasMaxLength(64);
            builder.Property(p => p.LastFailureMessage).HasMaxLength(512);
            builder.HasIndex(p => p.BuyerId);
        });

        modelBuilder.Entity<SavedCard>(builder =>
        {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Id).HasMaxLength(64);
            builder.Property(c => c.ShopperId).IsRequired().HasMaxLength(256);
            builder.Property(c => c.FinixPaymentInstrumentId).IsRequired().HasMaxLength(64);
            builder.Property(c => c.Brand).HasMaxLength(64);
            builder.Property(c => c.LastFour).HasMaxLength(4);
            builder.Property(c => c.RequestKey).HasMaxLength(128);
            builder.HasIndex(c => c.ShopperId);
            builder.HasIndex(c => new { c.ShopperId, c.RequestKey });
        });

        modelBuilder.Entity<ShopperProfile>(builder =>
        {
            builder.HasKey(p => p.ShopperId);
            builder.Property(p => p.ShopperId).HasMaxLength(256);
            builder.Property(p => p.FinixIdentityId).IsRequired().HasMaxLength(64);
        });

        modelBuilder.Entity<RefundRecord>(builder =>
        {
            builder.HasKey(r => r.Id);
            builder.Property(r => r.Id).HasMaxLength(64);
            builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);
            builder.Property(r => r.FinixTransferId).IsRequired().HasMaxLength(64);
            builder.Property(r => r.Status).HasMaxLength(32);
            builder.HasIndex(r => new { r.OrderId, r.IdempotencyKey }).IsUnique();
            builder.HasIndex(r => r.FinixTransferId);
        });

        modelBuilder.Entity<PaymentOperationClaim>(builder =>
        {
            builder.HasKey(c => c.Key);
            builder.Property(c => c.Key).HasMaxLength(256);
        });
    }
}
