using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<MaxioCustomer> MaxioCustomers => Set<MaxioCustomer>();
    public DbSet<SubscriptionEnrollment> SubscriptionEnrollments => Set<SubscriptionEnrollment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppIdentityDbContext).Assembly);
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
