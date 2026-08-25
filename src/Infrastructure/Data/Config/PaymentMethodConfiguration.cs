using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(p => p.BuyerId);

        builder.Property(p => p.VaultId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.CardBrand)
            .HasMaxLength(32);

        builder.Property(p => p.LastDigits)
            .HasMaxLength(4);

        builder.Property(p => p.Expiry)
            .HasMaxLength(7);
    }
}
