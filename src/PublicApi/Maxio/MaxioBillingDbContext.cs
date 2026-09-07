using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioBillingDbContext : DbContext
{
    public MaxioBillingDbContext(DbContextOptions<MaxioBillingDbContext> options) : base(options)
    {
    }

    public DbSet<MaxioCustomerMapping> MaxioCustomerMappings { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MaxioCustomerMapping>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.UserId).HasMaxLength(128);
            entity.Property(e => e.MaxioCustomerId).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");
        });
    }
}
