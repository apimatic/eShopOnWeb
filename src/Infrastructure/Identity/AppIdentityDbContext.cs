using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<MaxioSubscriptionLink> MaxioSubscriptionLinks => Set<MaxioSubscriptionLink>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
        builder.Entity<MaxioSubscriptionLink>(entity =>
        {
            entity.HasKey(link => link.Id);
            entity.Property(link => link.UserId).HasMaxLength(450).IsRequired();
            entity.Property(link => link.PlanHandle).HasMaxLength(255).IsRequired();
            entity.Property(link => link.SubscriptionReference).HasMaxLength(255).IsRequired();
            entity.HasOne(link => link.User)
                .WithMany()
                .HasForeignKey(link => link.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(link => link.SubscriptionReference).IsUnique();
        });
    }
}
