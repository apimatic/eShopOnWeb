using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.PaymentTokenId).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Last4).HasMaxLength(4);
        builder.Property(c => c.Brand).HasMaxLength(50);
        builder.Property(c => c.Expiry).HasMaxLength(10);
        builder.Property(c => c.CardholderName).HasMaxLength(200);
    }
}
