using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.SetupRequestId).IsRequired().HasMaxLength(108);
        builder.Property(x => x.TokenRequestId).IsRequired().HasMaxLength(108);
        builder.Property(x => x.PayPalSetupTokenId).HasMaxLength(64);
        builder.Property(x => x.PayPalPaymentTokenId).HasMaxLength(64);
        builder.Property(x => x.PayPalCustomerId).HasMaxLength(64);
        builder.Property(x => x.Brand).HasMaxLength(32);
        builder.Property(x => x.LastDigits).HasMaxLength(8);
        builder.Property(x => x.Expiry).HasMaxLength(16);
        builder.Property(x => x.CardholderName).HasMaxLength(128);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(32);
        builder.HasIndex(x => x.PayPalPaymentTokenId).IsUnique();
        builder.HasIndex(x => new { x.BuyerId, x.IsDeleted });
    }
}
