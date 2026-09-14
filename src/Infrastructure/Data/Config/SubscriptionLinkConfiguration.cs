using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionLinkConfiguration : IEntityTypeConfiguration<SubscriptionLink>
{
    public void Configure(EntityTypeBuilder<SubscriptionLink> builder)
    {
        builder.ToTable("SubscriptionLinks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(x => x.SubscriptionReference).IsRequired().HasMaxLength(80);
        builder.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
        builder.HasIndex(x => x.SubscriptionReference).IsUnique();
        builder.HasIndex(x => x.MaxioSubscriptionId).IsUnique().HasFilter("[MaxioSubscriptionId] IS NOT NULL");
    }
}
