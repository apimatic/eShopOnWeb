using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.HasKey(l => l.UserId);
        builder.Property(l => l.UserId).HasMaxLength(450);
        builder.Property(l => l.CustomerReference).IsRequired().HasMaxLength(256);
        builder.HasIndex(l => l.CustomerReference).IsUnique();
    }
}
