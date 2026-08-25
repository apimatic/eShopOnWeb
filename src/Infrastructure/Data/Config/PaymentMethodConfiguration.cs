using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(p => p.PayPalVaultId).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Brand).HasMaxLength(30);
        builder.Property(p => p.LastDigits).IsRequired().HasMaxLength(4);
        builder.Property(p => p.Expiry).IsRequired().HasMaxLength(7);
        builder.Property(p => p.CardholderName).HasMaxLength(200);
    }
}
