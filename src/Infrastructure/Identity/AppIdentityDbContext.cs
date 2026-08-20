using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<SubscriptionRequest> SubscriptionRequests => Set<SubscriptionRequest>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.FirstName).HasMaxLength(100);
            entity.Property(user => user.LastName).HasMaxLength(100);
        });

        builder.Entity<SubscriptionRequest>(entity =>
        {
            entity.ToTable("SubscriptionRequests");
            entity.HasKey(request => request.Id);
            entity.Property(request => request.UserId).HasMaxLength(450).IsRequired();
            entity.Property(request => request.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(request => request.ProductHandle).HasMaxLength(255).IsRequired();
            entity.Property(request => request.ProviderReference).HasMaxLength(100).IsRequired();
            entity.Property(request => request.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(request => request.LeaseOwner).HasMaxLength(64);
            entity.Property(request => request.Version).IsRowVersion();
            entity.HasIndex(request => new { request.UserId, request.IdempotencyKey }).IsUnique();
            entity.HasIndex(request => request.ProviderReference).IsUnique();
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(request => request.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
