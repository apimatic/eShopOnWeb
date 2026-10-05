using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.HasIndex(m => m.BuyerId);
        builder.HasIndex(m => new { m.BuyerId, m.SaveRequestKey }).IsUnique().HasFilter("[SaveRequestKey] IS NOT NULL");

        builder.Property(m => m.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(m => m.CardId).IsRequired().HasMaxLength(64);
        builder.Property(m => m.Alias).HasMaxLength(100);
        builder.Property(m => m.Last4).HasMaxLength(4);
        builder.Property(m => m.Brand).HasMaxLength(32);
        builder.Property(m => m.Expiry).HasMaxLength(7);
        builder.Property(m => m.ProviderCustomerId).HasMaxLength(64);
        builder.Property(m => m.SaveRequestKey).HasMaxLength(64);
        builder.Ignore(m => m.IsRemoved);
    }
}
