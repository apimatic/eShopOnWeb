using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(p => p.PaymentReference).HasMaxLength(32).IsRequired();
        builder.HasIndex(p => p.PaymentReference).IsUnique();
        builder.Property(p => p.ProviderTokenId).HasMaxLength(255);
        builder.Property(p => p.ProviderCustomerId).HasMaxLength(64);
        builder.Property(p => p.Brand).HasMaxLength(32);
        builder.Property(p => p.Last4).HasMaxLength(4);
        builder.Property(p => p.Expiry).HasMaxLength(7);
        builder.Property(p => p.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(p => new { p.BuyerId, p.State });
        builder.HasIndex(p => p.ProviderTokenId).IsUnique().HasFilter("[ProviderTokenId] IS NOT NULL");
    }
}
