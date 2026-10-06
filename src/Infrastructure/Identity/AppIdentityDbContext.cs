using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<MaxioCustomerMapping> MaxioCustomerMappings => Set<MaxioCustomerMapping>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);

        builder.Entity<MaxioCustomerMapping>(entity =>
        {
            entity.ToTable("MaxioCustomerMappings");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.UserId).HasMaxLength(450).IsRequired();
            entity.Property(m => m.MaxioCustomerReference).HasMaxLength(255).IsRequired();
            entity.HasIndex(m => m.UserId).IsUnique();
            entity.HasIndex(m => m.MaxioCustomerReference).IsUnique();
        });
    }
}
