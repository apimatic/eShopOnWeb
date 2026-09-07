using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<MaxioCustomerMapping> MaxioCustomerMappings { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MaxioCustomerMapping>()
            .HasIndex(m => m.UserId)
            .IsUnique();

        builder.Entity<MaxioCustomerMapping>()
            .HasIndex(m => m.MaxioCustomerId)
            .IsUnique();
    }
}
