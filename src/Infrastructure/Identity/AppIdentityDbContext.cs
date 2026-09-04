using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<SubscriptionIdempotencyRecord> SubscriptionIdempotencyRecords => Set<SubscriptionIdempotencyRecord>();

    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
        builder.Entity<SubscriptionIdempotencyRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Key).HasMaxLength(200).IsRequired();
            entity.Property(record => record.CustomerReference).HasMaxLength(200).IsRequired();
            entity.Property(record => record.SubscriptionReference).HasMaxLength(200).IsRequired();
            entity.Property(record => record.CreatedAtUtc).IsRequired();
            entity.Property(record => record.UpdatedAtUtc).IsRequired();
            entity.HasIndex(record => record.Key).IsUnique();
            entity.HasIndex(record => record.SubscriptionReference).IsUnique();
        });
    }
}
