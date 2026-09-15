using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("PaymentMethods");
        builder.Property(p => p.Alias).HasMaxLength(64);
        builder.Property(p => p.CardId).HasMaxLength(255);
        builder.Property(p => p.Last4).HasMaxLength(4);
        builder.Property(p => p.Brand).HasMaxLength(32);
        builder.Property(p => p.Expiry).HasMaxLength(7);
    }
}
