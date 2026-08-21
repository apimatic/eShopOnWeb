using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingSubscriptionConfiguration : IEntityTypeConfiguration<BillingSubscription>
{
    public void Configure(EntityTypeBuilder<BillingSubscription> builder)
    {
        builder.ToTable("BillingSubscriptions");

        builder.Property(subscription => subscription.UserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(subscription => subscription.ProductHandle)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(subscription => subscription.SubscriptionReference)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(subscription => subscription.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(subscription => subscription.UpdatedAt)
            .IsRequired();

        builder.HasIndex(subscription => new { subscription.UserId, subscription.ProductHandle })
            .IsUnique();

        builder.HasIndex(subscription => subscription.SubscriptionReference)
            .IsUnique();
    }
}
