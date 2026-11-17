using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperPaymentIdentityConfiguration : IEntityTypeConfiguration<ShopperPaymentIdentity>
{
    public void Configure(EntityTypeBuilder<ShopperPaymentIdentity> builder)
    {
        builder.HasKey(x => x.BuyerId);
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.FinixIdentityId).HasMaxLength(64);
    }
}
