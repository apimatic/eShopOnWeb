using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(m => m.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.FinixPaymentInstrumentId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(m => m.Brand).HasMaxLength(32);
        builder.Property(m => m.Last4).HasMaxLength(4);
        builder.Property(m => m.CardType).HasMaxLength(32);
        builder.Property(m => m.Name).HasMaxLength(256);

        builder.HasIndex(m => new { m.BuyerId, m.FinixPaymentInstrumentId }).IsUnique();
    }
}
