using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.FinixPaymentInstrumentId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.FinixIdentityId).HasMaxLength(64);
        builder.Property(p => p.Brand).HasMaxLength(64);
        builder.Property(p => p.Last4).HasMaxLength(4);
        builder.Property(p => p.CardholderName).HasMaxLength(200);

        builder.HasIndex(p => p.BuyerId);
        builder.HasIndex(p => new { p.BuyerId, p.FinixPaymentInstrumentId }).IsUnique();
    }
}
