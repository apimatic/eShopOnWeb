using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class CustomerPaymentMethodConfiguration : IEntityTypeConfiguration<CustomerPaymentMethod>
{
    public void Configure(EntityTypeBuilder<CustomerPaymentMethod> builder)
    {
        builder.Property(x => x.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.VaultId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Last4).HasMaxLength(4).IsRequired();
        builder.Property(x => x.CardBrand).HasMaxLength(50);
        builder.Property(x => x.ExpiryMonth).HasMaxLength(2);
        builder.Property(x => x.ExpiryYear).HasMaxLength(4);
        builder.Property(x => x.Alias).HasMaxLength(100);

        builder.HasIndex(x => x.BuyerId);
    }
}
