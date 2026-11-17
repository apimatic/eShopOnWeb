using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardClaimConfiguration : IEntityTypeConfiguration<SavedCardClaim>
{
    public void Configure(EntityTypeBuilder<SavedCardClaim> builder)
    {
        builder.HasKey(x => new { x.BuyerId, x.CardFingerprint });
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.CardFingerprint).IsRequired().HasMaxLength(128);
        builder.Property(x => x.PaymentMethodId).HasMaxLength(64);
    }
}
