using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(card => card.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(card => card.FinixPaymentInstrumentId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(card => card.Brand)
            .HasMaxLength(50);

        builder.Property(card => card.LastFour)
            .HasMaxLength(4);

        builder.HasIndex(card => card.OwnerId);
        builder.HasIndex(card => new { card.OwnerId, card.FinixPaymentInstrumentId })
            .IsUnique();
    }
}
