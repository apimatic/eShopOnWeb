using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(x => x.RequestId).IsRequired().HasMaxLength(36);
        builder.Property(x => x.PayPalPaymentTokenId).HasMaxLength(255);
        builder.Property(x => x.Brand).HasMaxLength(32);
        builder.Property(x => x.Last4).HasMaxLength(4);
        builder.Property(x => x.Expiry).HasMaxLength(7);
        builder.Property(x => x.CardholderName).HasMaxLength(300);
        builder.Ignore(x => x.IsActive);
        builder.HasIndex(x => x.PayPalPaymentTokenId).IsUnique().HasFilter("[PayPalPaymentTokenId] IS NOT NULL");
    }
}
