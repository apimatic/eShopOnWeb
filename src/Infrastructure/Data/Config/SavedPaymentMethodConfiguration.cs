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
            .HasMaxLength(128);

        builder.Property(m => m.PaypalCustomerId).HasMaxLength(128);
        builder.Property(m => m.LastDigits).HasMaxLength(4);
        builder.Property(m => m.Brand).HasMaxLength(32);
        builder.Property(m => m.Expiry).HasMaxLength(7);
    }
}
