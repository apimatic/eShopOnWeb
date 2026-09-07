using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<MaxioCustomer> MaxioCustomers { get; set; }
    public DbSet<Subscription> Subscriptions { get; set; }

    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MaxioCustomer>(b =>
        {
            b.ToTable("MaxioCustomers");
            b.HasKey(m => m.Id);
            b.Property(m => m.UserId).IsRequired().HasMaxLength(450);
            b.Property(m => m.MaxioCustomerId).IsRequired();
            b.HasIndex(m => m.UserId).IsUnique();
            b.HasIndex(m => m.MaxioCustomerId).IsUnique();
        });

        builder.Entity<Subscription>(b =>
        {
            b.ToTable("Subscriptions");
            b.HasKey(s => s.Id);
            b.Property(s => s.UserId).IsRequired().HasMaxLength(450);
            b.Property(s => s.MaxioCustomerId).IsRequired();
            b.Property(s => s.MaxioSubscriptionId).IsRequired();
            b.Property(s => s.ProductHandle).IsRequired().HasMaxLength(255);
            b.Property(s => s.State).IsRequired().HasMaxLength(50);
            b.HasIndex(s => s.UserId);
            b.HasIndex(s => s.MaxioSubscriptionId).IsUnique();
        });
    }
}
