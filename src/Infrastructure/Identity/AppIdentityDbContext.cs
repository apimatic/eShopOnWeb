using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<MaxioSubscriptionRecord> MaxioSubscriptionRecords => Set<MaxioSubscriptionRecord>();

    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<MaxioSubscriptionRecord>(entity =>
        {
            entity.HasIndex(record => new { record.UserId, record.PlanHandle }).IsUnique();
            entity.HasIndex(record => record.SubscriptionReference).IsUnique();
            entity.Property(record => record.UserId).HasMaxLength(450);
            entity.Property(record => record.PlanHandle).HasMaxLength(255);
            entity.Property(record => record.SubscriptionReference).HasMaxLength(255);
        });
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
