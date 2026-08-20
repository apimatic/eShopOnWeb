using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<MaxioCustomerLink> MaxioCustomerLinks => Set<MaxioCustomerLink>();
    public DbSet<MaxioSubscriptionLink> MaxioSubscriptionLinks => Set<MaxioSubscriptionLink>();

    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MaxioCustomerLink>(entity =>
        {
            entity.HasKey(link => link.UserId);
            entity.Property(link => link.CustomerReference).HasMaxLength(100).IsRequired();
            entity.HasIndex(link => link.CustomerReference).IsUnique();
            entity.HasIndex(link => link.MaxioCustomerId).IsUnique();
            entity.HasOne(link => link.User)
                .WithOne()
                .HasForeignKey<MaxioCustomerLink>(link => link.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MaxioSubscriptionLink>(entity =>
        {
            entity.HasKey(link => link.MaxioSubscriptionId);
            entity.Property(link => link.MaxioSubscriptionId).ValueGeneratedNever();
            entity.Property(link => link.ProductHandle).HasMaxLength(100).IsRequired();
            entity.Property(link => link.State).HasMaxLength(32).IsRequired();
            entity.HasIndex(link => new { link.UserId, link.ProductHandle });
            entity.HasOne(link => link.CustomerLink)
                .WithMany(customer => customer.Subscriptions)
                .HasForeignKey(link => link.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
