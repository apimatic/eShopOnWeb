using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(p => p.CardId).IsRequired().HasMaxLength(128);
        builder.Property(p => p.Alias).HasMaxLength(128);
        builder.Property(p => p.CardBrand).HasMaxLength(40);
        builder.Property(p => p.Last4).HasMaxLength(4);
        builder.Property(p => p.Expiry).HasMaxLength(10);
    }
}
