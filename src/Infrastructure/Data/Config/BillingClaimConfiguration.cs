using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingClaimAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingCustomerClaimConfiguration : IEntityTypeConfiguration<BillingCustomerClaim>
{
    public void Configure(EntityTypeBuilder<BillingCustomerClaim> builder)
    {
        builder.ToTable("BillingCustomerClaims");
        BillingClaimConfiguration.ConfigureCommon(builder);
    }
}

public class BillingSubscriptionClaimConfiguration : IEntityTypeConfiguration<BillingSubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<BillingSubscriptionClaim> builder)
    {
        builder.ToTable("BillingSubscriptionClaims");
        BillingClaimConfiguration.ConfigureCommon(builder);

        builder.Property(c => c.CustomerReference)
            .IsRequired()
            .HasMaxLength(BillingClaim.ReferenceMaxLength);

        builder.Property(c => c.PlanHandle)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(c => c.CustomerReference);
    }
}

internal static class BillingClaimConfiguration
{
    public static void ConfigureCommon<TClaim>(EntityTypeBuilder<TClaim> builder) where TClaim : BillingClaim
    {
        // The deterministic reference IS the primary key: the store refuses a second claim for it.
        builder.HasKey(c => c.Reference);

        builder.Property(c => c.Reference)
            .IsRequired()
            .HasMaxLength(BillingClaim.ReferenceMaxLength)
            .ValueGeneratedNever();

        builder.Property(c => c.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(c => c.ProviderId);
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();
        builder.Property(c => c.AttemptStartedAt).IsRequired();

        builder.Property(c => c.Version)
            .IsRequired()
            .IsConcurrencyToken();
    }
}
