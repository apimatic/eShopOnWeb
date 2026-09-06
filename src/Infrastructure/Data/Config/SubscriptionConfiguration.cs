using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.Property(s => s.UserId)
            .IsRequired()
            .HasMaxLength(36);

        builder.Property(s => s.ProductHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(s => s.State)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(s => new { s.UserId, s.MaxioSubscriptionId })
            .IsUnique();
    }
}
