using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(pm => pm.VaultId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(pm => pm.Brand)
            .HasMaxLength(50);

        builder.Property(pm => pm.Last4)
            .HasMaxLength(4);
    }
}
