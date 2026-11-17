using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.FinixIdentityId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.FinixPaymentInstrumentId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.Brand)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.LastFour)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(c => c.BuyerId);
        builder.HasIndex(c => c.FinixPaymentInstrumentId).IsUnique();
    }
}
