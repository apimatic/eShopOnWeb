using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<MaxioSubscriptionLink> MaxioSubscriptionLinks => Set<MaxioSubscriptionLink>();

    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<MaxioSubscriptionLink>(entity =>
        {
            entity.HasKey(link => link.Id);
            entity.Property(link => link.UserId).IsRequired().HasMaxLength(450);
            entity.Property(link => link.PlanHandle).IsRequired().HasMaxLength(255);
            entity.Property(link => link.SubscriptionReference).IsRequired().HasMaxLength(450);
            entity.Property(link => link.ProcessingToken).HasMaxLength(100);
            entity.Property(link => link.Status).IsRequired().HasMaxLength(32);
            entity.HasIndex(link => new { link.UserId, link.PlanHandle }).IsUnique();
            entity.HasIndex(link => link.SubscriptionReference).IsUnique();
        });
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
