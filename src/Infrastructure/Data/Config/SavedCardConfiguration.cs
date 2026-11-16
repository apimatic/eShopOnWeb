using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.FinixPaymentInstrumentId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.FinixIdentityId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.Alias).HasMaxLength(120);
        builder.Property(c => c.Brand).HasMaxLength(40);
        builder.Property(c => c.Last4).HasMaxLength(4);
        builder.Property(c => c.CardType).HasMaxLength(40);

        builder.HasIndex(c => new { c.BuyerId, c.FinixPaymentInstrumentId }).IsUnique();
    }
}
