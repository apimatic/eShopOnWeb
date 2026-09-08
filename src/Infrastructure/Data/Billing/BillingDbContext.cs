using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.Infrastructure.Data.Billing;

public class BillingDbContext : DbContext
{
    public BillingDbContext(DbContextOptions<BillingDbContext> options) : base(options)
    {
    }

    public DbSet<BillingAccount> BillingAccounts { get; set; }

    public DbSet<BillingSubscription> BillingSubscriptions { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<BillingAccount>(account =>
        {
            account.ToTable("BillingAccounts");
            account.HasKey(x => x.Id);
            account.Property(x => x.AppUserId).HasMaxLength(450).IsRequired();
            account.Property(x => x.AppUserName).HasMaxLength(256).IsRequired();
            account.Property(x => x.Email).HasMaxLength(256).IsRequired();
            account.Property(x => x.MaxioReference).HasMaxLength(255).IsRequired();
            account.Property(x => x.FirstName).HasMaxLength(128).IsRequired();
            account.Property(x => x.LastName).HasMaxLength(128).IsRequired();
            account.HasIndex(x => x.AppUserId).IsUnique();
            account.HasIndex(x => x.MaxioReference).IsUnique();
        });

        builder.Entity<BillingSubscription>(subscription =>
        {
            subscription.ToTable("BillingSubscriptions");
            subscription.HasKey(x => x.Id);
            subscription.Property(x => x.ProductHandle).HasMaxLength(255).IsRequired();
            subscription.HasIndex(x => x.MaxioSubscriptionId).IsUnique();
            subscription.HasOne(x => x.BillingAccount)
                .WithMany(x => x.Subscriptions)
                .HasForeignKey(x => x.BillingAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
