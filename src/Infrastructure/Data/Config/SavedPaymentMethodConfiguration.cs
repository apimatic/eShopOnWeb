using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.OwnerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.FinixPaymentInstrumentId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Brand).HasMaxLength(64);
        builder.Property(p => p.Last4).IsRequired().HasMaxLength(4);
        builder.Property(p => p.CardholderName).HasMaxLength(256);
        builder.Property(p => p.Alias).HasMaxLength(100);

        builder.HasIndex(p => p.OwnerId);
        builder.HasIndex(p => p.FinixPaymentInstrumentId).IsUnique();
    }
}
