using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.FinixIdentityId).IsRequired().HasMaxLength(255);
        builder.Property(c => c.FinixPaymentInstrumentId).IsRequired().HasMaxLength(255);
        builder.Property(c => c.Brand).HasMaxLength(64);
        builder.Property(c => c.Last4).HasMaxLength(4);
        builder.Property(c => c.CardType).HasMaxLength(64);

        builder.HasIndex(c => c.BuyerId);
        builder.HasIndex(c => new { c.BuyerId, c.FinixPaymentInstrumentId }).IsUnique();
    }
}
