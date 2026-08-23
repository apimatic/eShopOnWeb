using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<MaxioCustomerLink> MaxioCustomerLinks => Set<MaxioCustomerLink>();
    public DbSet<MaxioSubscriptionIntent> MaxioSubscriptionIntents => Set<MaxioSubscriptionIntent>();

    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MaxioCustomerLink>(entity =>
        {
            entity.ToTable("MaxioCustomerLinks");
            entity.Property(x => x.UserId).HasMaxLength(450);
            entity.Property(x => x.CustomerReference).HasMaxLength(100);
            entity.Property(x => x.Status).HasMaxLength(32);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.UserId).IsUnique();
            entity.HasIndex(x => x.CustomerReference).IsUnique();
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MaxioSubscriptionIntent>(entity =>
        {
            entity.ToTable("MaxioSubscriptionIntents");
            entity.Property(x => x.ProductHandle).HasMaxLength(100);
            entity.Property(x => x.CustomerReference).HasMaxLength(100);
            entity.Property(x => x.SubscriptionReference).HasMaxLength(100);
            entity.Property(x => x.Status).HasMaxLength(32);
            entity.Property(x => x.LastErrorCategory).HasMaxLength(64);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
            entity.HasIndex(x => x.SubscriptionReference).IsUnique();
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
