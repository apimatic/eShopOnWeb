using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingClaimConfiguration : IEntityTypeConfiguration<BillingClaim>
{
    public void Configure(EntityTypeBuilder<BillingClaim> builder)
    {
        builder.HasKey(claim => claim.Reference);

        builder.Property(claim => claim.Reference)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(claim => claim.Status)
            .IsRequired();

        builder.Property(claim => claim.UpdatedAt)
            .IsRequired();
    }
}
