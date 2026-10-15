using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.HasKey(l => l.UserName);
        builder.Property(l => l.UserName).HasMaxLength(256);
        builder.Property(l => l.Reference).IsRequired().HasMaxLength(320);
        builder.Property(l => l.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(l => l.ClaimedAt).IsConcurrencyToken();
    }
}
