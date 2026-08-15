using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.VaultAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class VaultedCardConfiguration : IEntityTypeConfiguration<VaultedCard>
{
    public void Configure(EntityTypeBuilder<VaultedCard> builder)
    {
        builder.Property(v => v.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(v => v.VaultId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(v => v.Brand).HasMaxLength(32);
        builder.Property(v => v.Last4).HasMaxLength(4);
        builder.Property(v => v.Expiry).HasMaxLength(7);
        builder.Property(v => v.Label).HasMaxLength(128);

        builder.HasIndex(v => v.BuyerId);
    }
}
