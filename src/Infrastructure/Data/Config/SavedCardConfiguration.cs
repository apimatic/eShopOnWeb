using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasMaxLength(64);
        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.FinixPaymentInstrumentId).IsRequired().HasMaxLength(64);
        builder.Property(c => c.BuyerIdentityId).HasMaxLength(64);
        builder.Property(c => c.Brand).HasMaxLength(64);
        builder.Property(c => c.LastFour).HasMaxLength(4);
        builder.Property(c => c.Name).HasMaxLength(256);
        builder.HasIndex(c => c.BuyerId);
    }
}
