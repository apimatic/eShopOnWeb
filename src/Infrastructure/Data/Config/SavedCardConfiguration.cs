using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.UserId).HasMaxLength(256).IsRequired();
        builder.Property(c => c.FinixPaymentInstrumentId).HasMaxLength(128).IsRequired();
        builder.Property(c => c.FinixIdentityId).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Brand).HasMaxLength(64);
        builder.Property(c => c.Last4).HasMaxLength(4);
        builder.Property(c => c.CardholderName).HasMaxLength(256);

        builder.HasIndex(c => new { c.UserId, c.FinixPaymentInstrumentId }).IsUnique();
        builder.HasIndex(c => c.UserId);
    }
}
