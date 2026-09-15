using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<MaxioSubscriptionLink> MaxioSubscriptionLinks => Set<MaxioSubscriptionLink>();

    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<MaxioSubscriptionLink>(entity =>
        {
            entity.HasIndex(link => new { link.UserId, link.ProductHandle }).IsUnique();
            entity.Property(link => link.UserId).HasMaxLength(450);
            entity.Property(link => link.ProductHandle).HasMaxLength(255);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(link => link.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
