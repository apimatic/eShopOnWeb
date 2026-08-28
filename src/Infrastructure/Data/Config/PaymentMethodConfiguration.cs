using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.PayPalVaultId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Brand).IsRequired().HasMaxLength(32);
        builder.Property(x => x.Last4).IsRequired().HasMaxLength(4);
        builder.Property(x => x.Expiry).HasMaxLength(7);
        builder.HasIndex(x => x.PayPalVaultId).IsUnique();
        builder.Ignore(x => x.IsDeleted);
    }
}
