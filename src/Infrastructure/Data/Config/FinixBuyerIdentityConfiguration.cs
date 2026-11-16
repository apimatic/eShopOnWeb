using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class FinixBuyerIdentityConfiguration : IEntityTypeConfiguration<FinixBuyerIdentity>
{
    public void Configure(EntityTypeBuilder<FinixBuyerIdentity> builder)
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
