using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("PaymentMethods");
        builder.Property(x => x.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ProviderPaymentTokenId).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => x.ProviderPaymentTokenId).IsUnique();
        builder.Property(x => x.ProviderCustomerId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Brand).HasMaxLength(32).IsRequired();
        builder.Property(x => x.LastDigits).HasMaxLength(4).IsRequired();
        builder.Property(x => x.Expiry).HasMaxLength(7).IsRequired();
        builder.Property(x => x.CardholderName).HasMaxLength(128);
        builder.Property(x => x.CardType).HasMaxLength(32);
        builder.HasIndex(x => new { x.BuyerId, x.IsActive });
    }
}
