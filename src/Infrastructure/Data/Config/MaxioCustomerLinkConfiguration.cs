using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.HasIndex(link => link.UserId).IsUnique();
        builder.HasIndex(link => link.CustomerReference).IsUnique();
        builder.Property(link => link.UserId).HasMaxLength(450).IsRequired();
        builder.Property(link => link.CustomerReference).HasMaxLength(100).IsRequired();
    }
}
