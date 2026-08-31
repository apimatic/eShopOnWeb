using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(x => x.OwnerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.CardId).IsRequired().HasMaxLength(255);
        builder.Property(x => x.Last4).IsRequired().HasMaxLength(4);
        builder.Property(x => x.Brand).IsRequired().HasMaxLength(30);
        builder.Property(x => x.Expiry).IsRequired().HasMaxLength(7);
        builder.Property(x => x.PayPalCustomerId).HasMaxLength(255);
        builder.Property(x => x.Alias).HasMaxLength(100);
        builder.HasIndex(x => x.CardId).IsUnique();
        builder.HasIndex(x => new { x.OwnerId, x.IsDeleted });
    }
}
