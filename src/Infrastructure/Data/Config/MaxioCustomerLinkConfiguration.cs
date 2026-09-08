using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.Property(x => x.BuyerId)
            .HasMaxLength(256)
            .IsRequired();

        builder.HasIndex(x => x.BuyerId)
            .IsUnique();

        builder.HasIndex(x => x.MaxioCustomerId)
            .IsUnique();
    }
}
