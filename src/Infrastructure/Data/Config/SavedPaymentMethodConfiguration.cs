using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");

        builder.Property(m => m.BuyerId)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(m => m.PayPalPaymentTokenId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(m => m.PayPalCustomerId)
            .HasMaxLength(64);

        builder.Property(m => m.Last4)
            .HasMaxLength(4)
            .IsRequired();

        builder.Property(m => m.Brand)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(m => m.Expiry)
            .HasMaxLength(7)
            .IsRequired();

        builder.Property(m => m.CardholderName)
            .HasMaxLength(120);

        builder.HasIndex(m => m.BuyerId);
    }
}
