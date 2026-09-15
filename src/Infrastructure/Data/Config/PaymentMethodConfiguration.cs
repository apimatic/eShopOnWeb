using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("PaymentMethods");
        builder.Property(p => p.OwnerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.PayPalCustomerId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.PayPalPaymentTokenId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Name).HasMaxLength(128);
        builder.Property(p => p.Brand).HasMaxLength(32);
        builder.Property(p => p.LastDigits).HasMaxLength(4);
        builder.Property(p => p.Expiry).HasMaxLength(7);
        builder.Property(p => p.Type).HasMaxLength(32);
        builder.Ignore(p => p.IsActive);
        builder.HasIndex(p => p.PayPalPaymentTokenId).IsUnique();
        builder.HasIndex(p => new { p.OwnerId, p.DeletedAt });
    }
}
