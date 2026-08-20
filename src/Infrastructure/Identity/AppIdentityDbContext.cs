using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<SubscriptionEnrollment> SubscriptionEnrollments => Set<SubscriptionEnrollment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<SubscriptionEnrollment>(enrollment =>
        {
            enrollment.ToTable("SubscriptionEnrollments");
            enrollment.HasKey(x => x.Id);
            enrollment.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            enrollment.Property(x => x.ProductHandle).HasMaxLength(255).IsRequired();
            enrollment.Property(x => x.Status).HasMaxLength(32).IsRequired();
            enrollment.Property(x => x.OperationId).HasMaxLength(32).IsRequired();
            enrollment.Property(x => x.ConcurrencyStamp).HasMaxLength(32).IsConcurrencyToken().IsRequired();
            enrollment.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
            enrollment.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
