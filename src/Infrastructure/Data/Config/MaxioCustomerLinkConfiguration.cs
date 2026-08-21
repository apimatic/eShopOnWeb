using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.Property(link => link.UserId).IsRequired().HasMaxLength(450);
        builder.Property(link => link.CustomerReference).IsRequired().HasMaxLength(80);
        builder.HasIndex(link => link.UserId).IsUnique();
        builder.HasIndex(link => link.CustomerReference).IsUnique();
    }
}
