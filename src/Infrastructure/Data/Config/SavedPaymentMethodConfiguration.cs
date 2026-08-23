using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");

        builder.Property(m => m.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.PaypalPaymentTokenId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(m => m.CardBrand)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(m => m.LastFourDigits)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(m => m.Expiry)
            .IsRequired()
            .HasMaxLength(7);

        builder.Property(m => m.CardholderName)
            .HasMaxLength(300);
    }
}
