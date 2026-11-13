using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingClaimConfiguration : IEntityTypeConfiguration<BillingClaim>
{
    public void Configure(EntityTypeBuilder<BillingClaim> builder)
    {
        builder.ToTable("BillingClaims");

        // The primary key is the claim: a second insert of the same key is refused by the store
        // (SQL Server and the in-memory provider alike), which is what makes subscribe idempotent.
        builder.HasKey(c => c.Key);
        builder.Property(c => c.Key)
            .HasMaxLength(BillingClaim.KeyMaxLength)
            .ValueGeneratedNever();

        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.UserName).HasMaxLength(256).IsRequired();
        builder.Property(c => c.ProviderReference).HasMaxLength(BillingClaim.ReferenceMaxLength).IsRequired();
        builder.Property(c => c.PlanHandle).HasMaxLength(BillingClaim.ReferenceMaxLength);
        builder.Property(c => c.Version).IsConcurrencyToken();

        builder.HasIndex(c => c.UserName);
    }
}
