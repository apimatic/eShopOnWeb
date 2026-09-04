using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("PaymentMethods");

        builder.Property(pm => pm.Alias).HasMaxLength(127);
        builder.Property(pm => pm.CardId).HasMaxLength(127);
        builder.Property(pm => pm.VaultId).HasMaxLength(127);
        builder.Property(pm => pm.Last4).HasMaxLength(4);
        builder.Property(pm => pm.Brand).HasMaxLength(32);
        builder.Property(pm => pm.Expiry).HasMaxLength(7);
    }
}
