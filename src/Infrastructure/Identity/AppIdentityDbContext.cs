using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.SubscriptionBilling;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<MaxioCustomerMapping> MaxioCustomerMappings => Set<MaxioCustomerMapping>();
    public DbSet<MaxioSubscriptionMapping> MaxioSubscriptionMappings => Set<MaxioSubscriptionMapping>();

    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new MaxioCustomerMappingConfiguration());
        builder.ApplyConfiguration(new MaxioSubscriptionMappingConfiguration());
    }
}
