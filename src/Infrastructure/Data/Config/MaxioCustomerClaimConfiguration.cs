using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerClaimConfiguration : IEntityTypeConfiguration<MaxioCustomerClaim>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerClaim> builder)
    {
        builder.HasKey(c => c.UserKey);
        builder.Property(c => c.UserKey).HasMaxLength(256);
    }
}
