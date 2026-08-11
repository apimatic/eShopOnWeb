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

        builder.Property(m => m.VaultId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(m => m.Brand).HasMaxLength(32);
        builder.Property(m => m.Last4).IsRequired().HasMaxLength(4);
        builder.Property(m => m.Expiry).HasMaxLength(7);
        builder.Property(m => m.CardholderName).HasMaxLength(256);
        builder.Property(m => m.Label).HasMaxLength(128);

        builder.HasIndex(m => m.BuyerId);
    }
}
