using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Maxio;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<Maxio.AppUserSubscription> AppUserSubscriptions { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);

        builder.Entity<Maxio.AppUserSubscription>(entity =>
        {
            entity.ToTable("AppUserSubscriptions");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.UserId).HasMaxLength(64);
            entity.Property(s => s.MaxioCustomerReference).HasMaxLength(128);
            entity.Property(s => s.ProductHandle).HasMaxLength(128);
            entity.Property(s => s.ProductName).HasMaxLength(256);
            entity.Property(s => s.Currency).HasMaxLength(8);
            entity.Property(s => s.State).HasMaxLength(32);
            entity.HasIndex(s => s.UserId);
            entity.HasIndex(s => new { s.UserId, s.ProductHandle, s.State });
            entity.HasIndex(s => s.MaxioSubscriptionId);
        });
    }
}
