using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<SubscriptionRecord> SubscriptionRecords => Set<SubscriptionRecord>();

    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<SubscriptionRecord>(record =>
        {
            record.ToTable("SubscriptionRecords");
            record.HasKey(x => x.Id);
            record.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            record.Property(x => x.ProductHandle).HasMaxLength(255).IsRequired();
            record.Property(x => x.SubscriptionReference).HasMaxLength(80).IsRequired();
            record.Property(x => x.Status).HasMaxLength(20).IsRequired();
            record.Property(x => x.Version).IsRowVersion();
            record.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
            record.HasIndex(x => x.SubscriptionReference).IsUnique();
            record.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
