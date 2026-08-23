using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionLinkConfiguration : IEntityTypeConfiguration<MaxioSubscriptionLink>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionLink> builder)
    {
        builder.HasIndex(link => new { link.UserId, link.ProductHandle }).IsUnique();
        builder.HasIndex(link => link.SubscriptionReference).IsUnique();
        builder.Property(link => link.UserId).HasMaxLength(450).IsRequired();
        builder.Property(link => link.ProductHandle).HasMaxLength(100).IsRequired();
        builder.Property(link => link.SubscriptionReference).HasMaxLength(100).IsRequired();
        builder.Property(link => link.IntegrationStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(link => link.FailureCode).HasMaxLength(100);
    }
}
