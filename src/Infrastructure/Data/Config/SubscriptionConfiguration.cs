using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.UserId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.MaxioSubscriptionId)
            .IsRequired();

        builder.Property(s => s.PlanHandle)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.PlanName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.PlanPrice)
            .HasPrecision(18, 2);

        builder.Property(s => s.State)
            .HasConversion<string>();

        builder.Property(s => s.CreatedDate)
            .IsRequired();

        builder.HasIndex(s => s.UserId)
            .HasDatabaseName("IX_Subscription_UserId");

        builder.HasIndex(s => s.MaxioSubscriptionId)
            .HasDatabaseName("IX_Subscription_MaxioSubscriptionId")
            .IsUnique();
    }
}
