using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.PayPalPaymentTokenId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.Brand)
            .HasMaxLength(32);

        builder.Property(p => p.Last4)
            .HasMaxLength(4);

        builder.Property(p => p.Expiry)
            .HasMaxLength(7);

        builder.HasIndex(p => p.BuyerId);
    }
}