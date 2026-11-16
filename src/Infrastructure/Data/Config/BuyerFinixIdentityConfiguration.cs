using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BuyerFinixIdentityConfiguration : IEntityTypeConfiguration<BuyerFinixIdentity>
{
    public void Configure(EntityTypeBuilder<BuyerFinixIdentity> builder)
    {
        builder.Property(i => i.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(i => i.FinixIdentityId)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(i => i.BuyerId).IsUnique();
    }
}
