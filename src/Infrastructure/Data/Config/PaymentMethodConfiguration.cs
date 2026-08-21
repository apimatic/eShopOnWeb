using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.VaultId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(p => p.CardBrand).HasMaxLength(30);
        builder.Property(p => p.LastFourDigits).IsRequired().HasMaxLength(4);
        builder.Property(p => p.Expiry).HasMaxLength(10);
        builder.Property(p => p.CardholderName).HasMaxLength(256);
    }
}
