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

        builder.Property(s => s.MaxioCustomerId)
            .IsRequired();

        builder.Property(s => s.MaxioSubscriptionId)
            .IsRequired();

        builder.Property(s => s.PlanHandle)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.NextBillingDate)
            .HasColumnType("datetime2");

        builder.Property(s => s.CreatedAt)
            .HasColumnType("datetime2")
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .HasColumnType("datetime2")
            .IsRequired();

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.MaxioSubscriptionId).IsUnique();
    }
}
