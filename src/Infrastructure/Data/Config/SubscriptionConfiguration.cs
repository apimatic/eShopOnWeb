using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("Subscriptions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.UserId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(s => s.PlanHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(s => s.PlanName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(s => s.State)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(s => new { s.UserId, s.MaxioSubscriptionId })
            .IsUnique();

        builder.HasIndex(s => s.SubscriptionReference);
    }
}
