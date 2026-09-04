using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<MaxioSubscriptionRecord> MaxioSubscriptionRecords { get; set; }
    public DbSet<MaxioCustomerRecord> MaxioCustomerRecords { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<MaxioCustomerRecord>(entity =>
        {
            entity.HasKey(record => record.UserId);
            entity.Property(record => record.UserId).HasMaxLength(450);
            entity.Property(record => record.CustomerReference).HasMaxLength(255).IsRequired();
            entity.HasIndex(record => record.CustomerReference).IsUnique();
        });

        builder.Entity<MaxioSubscriptionRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.Property(record => record.UserId).HasMaxLength(450).IsRequired();
            entity.Property(record => record.ProductHandle).HasMaxLength(255).IsRequired();
            entity.Property(record => record.SubscriptionReference).HasMaxLength(255).IsRequired();
            entity.HasIndex(record => record.SubscriptionReference).IsUnique();
            entity.HasIndex(record => new { record.UserId, record.ProductHandle }).IsUnique();
        });
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
