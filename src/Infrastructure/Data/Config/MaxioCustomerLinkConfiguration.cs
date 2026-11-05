using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        // The primary key is the claim: the store refuses a second customer for the same user.
        builder.HasKey(l => l.UserName);
        builder.Property(l => l.UserName).HasMaxLength(256);
        builder.Property(l => l.CustomerReference).IsRequired().HasMaxLength(256);
    }
}
