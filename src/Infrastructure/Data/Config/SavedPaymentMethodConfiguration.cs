using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(card => card.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(card => card.FinixPaymentInstrumentId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(card => card.FinixIdentityId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(card => card.Brand)
            .HasMaxLength(64);

        builder.Property(card => card.Last4)
            .HasMaxLength(4);

        builder.Property(card => card.Name)
            .HasMaxLength(256);

        builder.HasIndex(card => new { card.BuyerId, card.FinixPaymentInstrumentId })
            .IsUnique();
    }
}
