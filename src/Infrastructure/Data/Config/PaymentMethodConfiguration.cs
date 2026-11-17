using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("PaymentMethods");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(p => p.FinixIdentityId).HasMaxLength(255).IsRequired();
        builder.Property(p => p.CardId).HasMaxLength(255).IsRequired();
        builder.Property(p => p.Brand).HasMaxLength(64);
        builder.Property(p => p.Last4).HasMaxLength(4);
        builder.Property(p => p.CardType).HasMaxLength(32);
        builder.Property(p => p.Alias).HasMaxLength(128);

        builder.HasIndex(p => new { p.BuyerId, p.CardId });
    }
}
