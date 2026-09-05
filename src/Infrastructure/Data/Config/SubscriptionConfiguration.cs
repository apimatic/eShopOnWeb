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
            .HasMaxLength(450);

        builder.Property(s => s.ProductHandle)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.ProductName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(s => s.State)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.PriceInCents)
            .HasPrecision(19, 0);

        builder.HasIndex(s => new { s.UserId, s.MaxioSubscriptionId })
            .IsUnique()
            .HasDatabaseName("IX_Subscriptions_UserId_MaxioSubscriptionId");

        builder.HasIndex(s => s.MaxioCustomerId)
            .HasDatabaseName("IX_Subscriptions_MaxioCustomerId");
    }
}
