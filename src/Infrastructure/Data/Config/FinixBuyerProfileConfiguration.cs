using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class FinixBuyerProfileConfiguration : IEntityTypeConfiguration<FinixBuyerProfile>
{
    public void Configure(EntityTypeBuilder<FinixBuyerProfile> builder)
    {
        builder.HasKey(p => p.BuyerId);

        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.FinixIdentityId)
            .IsRequired()
            .HasMaxLength(64);
    }
}
