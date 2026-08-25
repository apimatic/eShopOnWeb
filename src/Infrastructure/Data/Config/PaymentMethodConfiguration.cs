using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(p => p.VaultId).IsRequired().HasMaxLength(255);
        builder.Property(p => p.Brand).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Last4).IsRequired().HasMaxLength(4);
        builder.Property(p => p.ExpiryYearMonth).IsRequired().HasMaxLength(7);
        builder.Property(p => p.Alias).HasMaxLength(100);
    }
}
