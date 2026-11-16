using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class FinixCustomerProfileConfiguration : IEntityTypeConfiguration<FinixCustomerProfile>
{
    public void Configure(EntityTypeBuilder<FinixCustomerProfile> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.FinixIdentityId).IsRequired().HasMaxLength(64);
        builder.HasIndex(p => p.BuyerId).IsUnique();
    }
}
