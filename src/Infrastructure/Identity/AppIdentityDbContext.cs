using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
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

        builder.Entity<MaxioCustomerMapping>(entity =>
        {
            entity.HasKey(mapping => mapping.UserId);
            entity.Property(mapping => mapping.UserId).HasMaxLength(450);
            entity.Property(mapping => mapping.MaxioCustomerId).IsRequired();
            entity.ToTable("MaxioCustomerMappings");
        });

        builder.Entity<MaxioSubscriptionMapping>(entity =>
        {
            entity.HasKey(mapping => mapping.Id);
            entity.Property(mapping => mapping.UserId).HasMaxLength(450).IsRequired();
            entity.Property(mapping => mapping.ProductHandle).HasMaxLength(200).IsRequired();
            entity.Property(mapping => mapping.Reference).HasMaxLength(450).IsRequired();
            entity.Property(mapping => mapping.CreatedAt).IsRequired();
            entity.HasIndex(mapping => mapping.MaxioSubscriptionId).IsUnique();
            entity.HasIndex(mapping => new { mapping.UserId, mapping.Reference }).IsUnique();
            entity.ToTable("MaxioSubscriptionMappings");
        });
    }

    public DbSet<MaxioCustomerMapping> MaxioCustomerMappings => Set<MaxioCustomerMapping>();
    public DbSet<MaxioSubscriptionMapping> MaxioSubscriptionMappings => Set<MaxioSubscriptionMapping>();
}
