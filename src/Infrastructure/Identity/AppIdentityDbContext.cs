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

        builder.Entity<MaxioSubscriptionLink>(entity =>
        {
            entity.ToTable("MaxioSubscriptionLinks");
            entity.HasKey(link => link.Id);
            entity.Property(link => link.UserId).HasMaxLength(450).IsRequired();
            entity.Property(link => link.ProductHandle).HasMaxLength(255).IsRequired();
            entity.Property(link => link.SubscriptionReference).HasMaxLength(80).IsRequired();
            entity.Property(link => link.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(link => link.LeaseOwner).HasMaxLength(64);
            entity.HasIndex(link => new { link.UserId, link.ProductHandle }).IsUnique();
            entity.HasIndex(link => link.SubscriptionReference).IsUnique();
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(link => link.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public DbSet<MaxioSubscriptionLink> MaxioSubscriptionLinks => Set<MaxioSubscriptionLink>();
}
