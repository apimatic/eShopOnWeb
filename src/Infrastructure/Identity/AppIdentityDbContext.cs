using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<MaxioCustomerRecord> MaxioCustomers => Set<MaxioCustomerRecord>();
    public DbSet<MaxioSubscriptionRecord> MaxioSubscriptions => Set<MaxioSubscriptionRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MaxioCustomerRecord>(entity =>
        {
            entity.ToTable("MaxioCustomers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.SiteSubdomain).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CustomerReference).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.SiteSubdomain }).IsUnique();
            entity.HasIndex(x => new { x.SiteSubdomain, x.CustomerReference }).IsUnique();
        });

        builder.Entity<MaxioSubscriptionRecord>(entity =>
        {
            entity.ToTable("MaxioSubscriptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.SiteSubdomain).HasMaxLength(100).IsRequired();
            entity.Property(x => x.SubscriptionReference).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ProductHandle).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.State).HasMaxLength(40).IsRequired();
            entity.Property(x => x.IntervalUnit).HasMaxLength(20).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.SiteSubdomain, x.ProductHandle }).IsUnique();
            entity.HasIndex(x => new { x.SiteSubdomain, x.SubscriptionReference }).IsUnique();
        });
    }
}
