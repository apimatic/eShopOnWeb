using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.HasKey(c => c.PaymentMethodId);
        builder.Property(c => c.PaymentMethodId).HasMaxLength(64);
        builder.Property(c => c.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(c => c.FinixIdentityId).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Last4).HasMaxLength(4);
        builder.Property(c => c.Brand).HasMaxLength(32);
        builder.HasIndex(c => c.BuyerId);
    }
}
