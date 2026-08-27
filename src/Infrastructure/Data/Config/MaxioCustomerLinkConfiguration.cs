using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.ToTable("MaxioCustomerLinks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.CustomerReference).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasIndex(x => x.CustomerReference).IsUnique();
    }
}
