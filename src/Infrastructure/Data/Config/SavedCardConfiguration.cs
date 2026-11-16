using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.ToTable("SavedCards");

        builder.Property(card => card.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(card => card.FinixPaymentInstrumentId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(card => card.Brand)
            .HasMaxLength(32);

        builder.Property(card => card.LastFour)
            .HasMaxLength(4);

        builder.Property(card => card.Name)
            .HasMaxLength(100);

        builder.HasIndex(card => card.OwnerId);
        builder.HasIndex(card => card.FinixPaymentInstrumentId)
            .IsUnique();
    }
}
