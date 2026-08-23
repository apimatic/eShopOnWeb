using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<MaxioSubscriptionClaim> MaxioSubscriptionClaims => Set<MaxioSubscriptionClaim>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<MaxioSubscriptionClaim>(entity =>
        {
            entity.ToTable("MaxioSubscriptionClaims");
            entity.HasKey(claim => claim.Id);
            entity.Property(claim => claim.UserId).HasMaxLength(450).IsRequired();
            entity.Property(claim => claim.ProductHandle).HasMaxLength(255).IsRequired();
            entity.Property(claim => claim.SubscriptionReference).HasMaxLength(255).IsRequired();
            entity.Property(claim => claim.LeaseToken).HasMaxLength(64).IsRequired().IsConcurrencyToken();
            entity.Property(claim => claim.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(claim => new { claim.UserId, claim.ProductHandle }).IsUnique();
            entity.HasIndex(claim => claim.SubscriptionReference).IsUnique();
        });
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
