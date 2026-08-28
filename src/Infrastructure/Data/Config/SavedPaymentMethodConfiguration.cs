using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(x => x.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.CreateRequestId).HasMaxLength(108).IsRequired();
        builder.Property(x => x.PayPalVaultId).HasMaxLength(128);
        builder.Property(x => x.CardholderName).HasMaxLength(128);
        builder.Property(x => x.Brand).HasMaxLength(32);
        builder.Property(x => x.LastDigits).HasMaxLength(8);
        builder.Property(x => x.Expiry).HasMaxLength(16);
        builder.Property(x => x.Type).HasMaxLength(32);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.PayPalVaultId).IsUnique().HasFilter("[PayPalVaultId] IS NOT NULL");
        builder.HasIndex(x => new { x.BuyerId, x.IsActive });
    }
}
