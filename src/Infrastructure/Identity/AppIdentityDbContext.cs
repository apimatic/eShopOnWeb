using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<MaxioCustomerLink> MaxioCustomerLinks { get; set; } = default!;

    public DbSet<MaxioSubscriptionLink> MaxioSubscriptionLinks { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);

        builder.Entity<MaxioCustomerLink>(entity =>
        {
            entity.HasKey(link => link.EShopUserId);
            entity.Property(link => link.MaxioReference).IsRequired().HasMaxLength(100);
        });

        builder.Entity<MaxioSubscriptionLink>(entity =>
        {
            entity.HasKey(link => new { link.EShopUserId, link.PlanHandle });
            entity.Property(link => link.PlanHandle).IsRequired().HasMaxLength(200);
            entity.Property(link => link.MaxioReference).IsRequired().HasMaxLength(300);
        });
    }
}
