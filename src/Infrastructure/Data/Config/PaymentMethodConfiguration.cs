using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.HasIndex(x => x.PayPalTokenId).IsUnique();
        builder.Property(x => x.PayPalTokenId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Brand).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Last4).HasMaxLength(4).IsRequired();
        builder.Property(x => x.Expiry).HasMaxLength(7).IsRequired();
        builder.Property(x => x.CardholderName).HasMaxLength(300);
        builder.Ignore(x => x.IsActive);
    }
}
