using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(p => p.OwnerId).HasMaxLength(256).IsRequired();
        builder.Property(p => p.ProviderTokenId).HasMaxLength(255).IsRequired();
        builder.Property(p => p.Brand).HasMaxLength(32).IsRequired();
        builder.Property(p => p.Last4).HasMaxLength(4).IsRequired();
        builder.Property(p => p.Expiry).HasMaxLength(7).IsRequired();
        builder.HasIndex(p => p.ProviderTokenId).IsUnique();
        builder.HasIndex(p => new { p.OwnerId, p.CreatedAt });
    }
}
