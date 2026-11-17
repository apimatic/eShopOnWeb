using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.HasKey(card => card.Id);

        builder.Property(card => card.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(card => card.FinixPaymentInstrumentId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(card => card.Brand)
            .HasMaxLength(50);

        builder.Property(card => card.Last4)
            .HasMaxLength(4);

        builder.Property(card => card.CardholderName)
            .HasMaxLength(200);

        builder.Property(card => card.FinixIdentityId)
            .HasMaxLength(100);

        builder.HasIndex(card => card.BuyerId);
    }
}
