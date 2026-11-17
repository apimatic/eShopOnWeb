using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(m => m.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.FinixPaymentInstrumentId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(m => m.Brand).HasMaxLength(64);
        builder.Property(m => m.LastFour).HasMaxLength(4);
        builder.Property(m => m.CardholderName).HasMaxLength(256);
        builder.Property(m => m.Fingerprint).HasMaxLength(128);

        builder.HasIndex(m => m.OwnerId);
        builder.HasIndex(m => m.FinixPaymentInstrumentId).IsUnique();
    }
}
