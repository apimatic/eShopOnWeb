using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionLinkConfiguration : IEntityTypeConfiguration<SubscriptionLink>
{
    public void Configure(EntityTypeBuilder<SubscriptionLink> builder)
    {
        builder.ToTable("SubscriptionLinks");
        builder.HasKey(link => link.Id);
        builder.Property(link => link.Id).ValueGeneratedOnAdd();
        builder.Property(link => link.UserId).IsRequired().HasMaxLength(450);
        builder.Property(link => link.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(link => link.CustomerReference).IsRequired().HasMaxLength(255);
        builder.Property(link => link.SubscriptionReference).IsRequired().HasMaxLength(255);
        builder.HasIndex(link => new { link.UserId, link.ProductHandle }).IsUnique();
        builder.HasIndex(link => link.SubscriptionReference).IsUnique();
    }
}
