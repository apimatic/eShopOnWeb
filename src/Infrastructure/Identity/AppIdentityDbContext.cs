using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<MaxioSubscriptionRecord> MaxioSubscriptions => Set<MaxioSubscriptionRecord>();
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<MaxioSubscriptionRecord>(entity =>
        {
            entity.HasIndex(record => new { record.ApplicationUserId, record.PlanHandle }).IsUnique();
            entity.Property(record => record.ApplicationUserId).IsRequired();
            entity.Property(record => record.PlanHandle).HasMaxLength(255).IsRequired();
            entity.Property(record => record.SubscriptionReference).HasMaxLength(512).IsRequired();
        });
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
