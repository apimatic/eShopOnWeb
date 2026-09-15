using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<MaxioCustomerLink> MaxioCustomerLinks => Set<MaxioCustomerLink>();
    public DbSet<MaxioSubscriptionEnrollment> MaxioSubscriptionEnrollments => Set<MaxioSubscriptionEnrollment>();

    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MaxioCustomerLink>(entity =>
        {
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.UserId).HasMaxLength(450);
            entity.HasIndex(x => x.MaxioCustomerId).IsUnique();
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MaxioSubscriptionEnrollment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserId).HasMaxLength(450);
            entity.Property(x => x.ProductHandle).HasMaxLength(255);
            entity.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
            entity.HasIndex(x => x.MaxioSubscriptionId).IsUnique();
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
