using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(x => x.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.PayPalPaymentTokenId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.PayPalCustomerId).HasMaxLength(128);
        builder.Property(x => x.Brand).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Last4).HasMaxLength(4).IsRequired();
        builder.Property(x => x.Expiry).HasMaxLength(7).IsRequired();
        builder.HasIndex(x => new { x.BuyerId, x.PayPalPaymentTokenId }).IsUnique();
    }
}
