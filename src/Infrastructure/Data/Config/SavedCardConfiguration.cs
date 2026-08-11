using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.VaultId)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(c => c.BuyerId);

        builder.Property(c => c.Brand).HasMaxLength(32);
        builder.Property(c => c.Last4).HasMaxLength(4);
        builder.Property(c => c.ExpiryMonth).HasMaxLength(2);
        builder.Property(c => c.ExpiryYear).HasMaxLength(4);
        builder.Property(c => c.Label).HasMaxLength(64);
    }
}
