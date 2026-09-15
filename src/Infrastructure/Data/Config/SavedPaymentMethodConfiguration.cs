using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(x => x.OwnerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.PayPalCustomerId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.PayPalPaymentTokenId).IsRequired().HasMaxLength(64);
        builder.HasIndex(x => x.PayPalPaymentTokenId).IsUnique();
        builder.Property(x => x.Brand).IsRequired().HasMaxLength(32);
        builder.Property(x => x.Last4).IsRequired().HasMaxLength(4);
        builder.Property(x => x.Expiry).IsRequired().HasMaxLength(7);
    }
}
