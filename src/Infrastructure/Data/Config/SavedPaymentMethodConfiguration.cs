using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");

        builder.Property(m => m.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.PayPalCustomerId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(m => m.PayPalPaymentTokenId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(m => m.CardBrand)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(m => m.Last4)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(m => m.Expiry).HasMaxLength(7);
        builder.Property(m => m.CardholderName).HasMaxLength(256);

        builder.HasIndex(m => m.OwnerId);
    }
}
