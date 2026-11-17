using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.HasIndex(c => c.FinixPaymentInstrumentId).IsUnique();
        builder.HasIndex(c => c.BuyerId);

        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.FinixPaymentInstrumentId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.Brand)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.Last4)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(c => c.CardholderName)
            .HasMaxLength(256);
    }
}
