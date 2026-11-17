using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.HasKey(x => new { x.BuyerId, x.CardFingerprint });
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.CardFingerprint).IsRequired().HasMaxLength(128);
        builder.Property(x => x.Id).IsRequired().HasMaxLength(64);
        builder.Property(x => x.FinixPaymentInstrumentId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Brand).HasMaxLength(64);
        builder.Property(x => x.LastFour).HasMaxLength(4);
        builder.HasIndex(x => x.Id);
    }
}
