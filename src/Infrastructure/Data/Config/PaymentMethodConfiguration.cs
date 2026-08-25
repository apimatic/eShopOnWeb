using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.PayPalVaultId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.LastFour)
            .HasMaxLength(4);

        builder.Property(p => p.Brand)
            .HasMaxLength(50);

        builder.Property(p => p.Expiry)
            .HasMaxLength(10);

        builder.Property(p => p.CardholderName)
            .HasMaxLength(200);
    }
}
