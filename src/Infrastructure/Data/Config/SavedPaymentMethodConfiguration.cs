using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.HasIndex(m => new { m.BuyerId, m.FinixPaymentInstrumentId }).IsUnique();

        builder.Property(m => m.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.FinixIdentityId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(m => m.FinixPaymentInstrumentId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(m => m.Brand).HasMaxLength(64);
        builder.Property(m => m.Last4).HasMaxLength(4);
        builder.Property(m => m.CardholderName).HasMaxLength(256);
    }
}
