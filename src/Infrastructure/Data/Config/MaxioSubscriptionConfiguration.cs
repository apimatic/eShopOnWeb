using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscription;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionConfiguration : IEntityTypeConfiguration<MaxioSubscription>
{
    public void Configure(EntityTypeBuilder<MaxioSubscription> builder)
    {
        builder.Property(s => s.UserId).IsRequired().HasMaxLength(450);
        builder.Property(s => s.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(s => s.State).IsRequired().HasMaxLength(50);
        builder.Property(s => s.MaxioCustomerId).IsRequired();
        builder.Property(s => s.MaxioSubscriptionId).IsRequired();

        builder.HasIndex(s => new { s.UserId, s.MaxioSubscriptionId }).IsUnique();
        builder.HasIndex(s => s.MaxioSubscriptionId).IsUnique();
    }
}
