using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionLinkConfiguration : IEntityTypeConfiguration<SubscriptionLink>
{
    public void Configure(EntityTypeBuilder<SubscriptionLink> builder)
    {
        builder.ToTable("SubscriptionLinks");

        builder.Property(s => s.UserId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.ProductHandle)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(s => s.SubscriptionReference).IsUnique();
        builder.HasIndex(s => s.UserId);
    }
}