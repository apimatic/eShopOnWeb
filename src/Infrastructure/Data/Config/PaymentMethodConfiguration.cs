using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(p => p.PayPalVaultId).IsRequired().HasMaxLength(128);
        builder.Property(p => p.Alias).HasMaxLength(128);
        builder.Property(p => p.Brand).HasMaxLength(32);
        builder.Property(p => p.Last4).HasMaxLength(4);
    }
}
