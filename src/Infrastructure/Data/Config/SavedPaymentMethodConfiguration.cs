using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(x => x.OwnerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.RequestId).IsRequired().HasMaxLength(108);
        builder.Property(x => x.PayPalTokenId).HasMaxLength(64);
        builder.Property(x => x.PayPalCustomerId).HasMaxLength(64);
        builder.Property(x => x.Brand).HasMaxLength(32);
        builder.Property(x => x.LastDigits).HasMaxLength(4);
        builder.Property(x => x.Expiry).HasMaxLength(7);
        builder.Property(x => x.CardType).HasMaxLength(32);
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasIndex(x => x.PayPalTokenId).IsUnique().HasFilter("[PayPalTokenId] IS NOT NULL");
        builder.HasIndex(x => new { x.OwnerId, x.IsDeleted });
    }
}
