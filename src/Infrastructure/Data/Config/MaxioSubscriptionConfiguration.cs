using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class MaxioSubscriptionConfiguration : IEntityTypeConfiguration<MaxioSubscription>
{
    public void Configure(EntityTypeBuilder<MaxioSubscription> builder)
    {
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.SubscriptionReference).IsRequired().HasMaxLength(255);
        builder.Property(x => x.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(x => x.ProductName).IsRequired().HasMaxLength(255);
        builder.Property(x => x.IntervalUnit).IsRequired().HasMaxLength(20);
        builder.Property(x => x.State).IsRequired().HasMaxLength(40);

        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasIndex(x => x.MaxioSubscriptionId).IsUnique();
        builder.HasIndex(x => x.SubscriptionReference).IsUnique();
    }
}
