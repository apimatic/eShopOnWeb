using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscription;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<MaxioCustomer> MaxioCustomers { get; set; }
    public DbSet<UserSubscription> UserSubscriptions { get; set; }

    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MaxioCustomer>()
            .HasKey(x => x.Id);
        builder.Entity<MaxioCustomer>()
            .HasIndex(x => x.UserId)
            .IsUnique();

        builder.Entity<UserSubscription>()
            .HasKey(x => x.Id);
        builder.Entity<UserSubscription>()
            .HasIndex(x => new { x.UserId, x.MaxioSubscriptionId })
            .IsUnique();
    }
}
