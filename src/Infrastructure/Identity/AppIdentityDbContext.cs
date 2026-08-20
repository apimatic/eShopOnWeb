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

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(x => x.FirstName).HasMaxLength(100);
            user.Property(x => x.LastName).HasMaxLength(100);
        });

        builder.Entity<SubscriptionEnrollment>(enrollment =>
        {
            enrollment.ToTable("SubscriptionEnrollments");
            enrollment.HasKey(x => x.Id);
            enrollment.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            enrollment.Property(x => x.ProductHandle).HasMaxLength(255).IsRequired();
            enrollment.Property(x => x.MaxioSubscriptionReference).HasMaxLength(255).IsRequired();
            enrollment.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
            enrollment.Property(x => x.RowVersion).IsRowVersion();
            enrollment.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
            enrollment.HasIndex(x => x.MaxioSubscriptionReference).IsUnique();
            enrollment.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
