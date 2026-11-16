using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(m => m.UserId).HasMaxLength(256).IsRequired();
        builder.Property(m => m.FinixPaymentInstrumentId).HasMaxLength(64).IsRequired();
        builder.Property(m => m.Brand).HasMaxLength(64);
        builder.Property(m => m.Last4).HasMaxLength(4);

        builder.HasIndex(m => new { m.UserId, m.FinixPaymentInstrumentId }).IsUnique();
    }
}
