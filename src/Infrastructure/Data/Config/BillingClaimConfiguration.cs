using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingCustomerClaimConfiguration : IEntityTypeConfiguration<BillingCustomerClaim>
{
    public void Configure(EntityTypeBuilder<BillingCustomerClaim> builder)
    {
        BillingClaimConfiguration.ConfigureCommon(builder);
    }
}

public class BillingSubscriptionClaimConfiguration : IEntityTypeConfiguration<BillingSubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<BillingSubscriptionClaim> builder)
    {
        BillingClaimConfiguration.ConfigureCommon(builder);

        builder.Property(c => c.PlanHandle)
            .IsRequired()
            .HasMaxLength(255);
    }
}

internal static class BillingClaimConfiguration
{
    public static void ConfigureCommon<T>(EntityTypeBuilder<T> builder) where T : BillingClaim
    {
        // The primary key is what refuses a second concurrent claim for the same user.
        builder.HasKey(c => c.UserKey);

        builder.Property(c => c.UserKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.ProviderReference)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(c => c.Version)
            .IsConcurrencyToken();
    }
}
