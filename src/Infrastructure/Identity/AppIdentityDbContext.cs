using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<SubscriptionIdempotencyRecord> SubscriptionIdempotencyRecords => Set<SubscriptionIdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.FirstName).HasMaxLength(100);
            entity.Property(user => user.LastName).HasMaxLength(100);
        });

        builder.Entity<SubscriptionIdempotencyRecord>(entity =>
        {
            entity.ToTable("SubscriptionIdempotencyRecords");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.UserId).HasMaxLength(450).IsRequired();
            entity.Property(record => record.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(record => record.ProductHandle).HasMaxLength(100).IsRequired();
            entity.Property(record => record.SubscriptionReference).HasMaxLength(100).IsRequired();
            entity.Property(record => record.Status).HasMaxLength(20).IsRequired();
            entity.HasIndex(record => new { record.UserId, record.IdempotencyKey }).IsUnique();
            entity.HasIndex(record => new { record.UserId, record.ProductHandle }).IsUnique();
            entity.HasIndex(record => record.SubscriptionReference).IsUnique();
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(record => record.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
