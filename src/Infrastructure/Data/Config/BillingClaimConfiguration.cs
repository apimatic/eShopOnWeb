using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingClaimConfiguration : IEntityTypeConfiguration<BillingClaim>
{
    public void Configure(EntityTypeBuilder<BillingClaim> builder)
    {
        builder.ToTable("BillingClaims");

        // The primary key is what refuses a second claim for the same provider write. Every provider
        // (SQL Server and EF InMemory alike) enforces a primary key, unlike a unique index.
        builder.HasKey(c => c.Key);
        builder.Property(c => c.Key)
            .HasMaxLength(BillingClaim.KeyMaxLength)
            .ValueGeneratedNever();

        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.State).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.ExternalId).HasMaxLength(50);
        builder.Property(c => c.PlanHandle).HasMaxLength(255);
        builder.Property(c => c.Version).IsConcurrencyToken();
    }
}
