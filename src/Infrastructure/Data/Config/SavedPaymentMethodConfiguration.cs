using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");

        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.VaultId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(p => p.CardBrand).HasMaxLength(30);
        builder.Property(p => p.LastFourDigits).HasMaxLength(4);
        builder.Property(p => p.Expiry).HasMaxLength(7);

        builder.HasIndex(p => p.BuyerId);
    }
}
