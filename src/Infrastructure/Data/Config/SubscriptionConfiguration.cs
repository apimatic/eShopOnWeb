using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.UserId).IsRequired();
        builder.Property(s => s.MaxioCustomerId).IsRequired();
        builder.Property(s => s.MaxioSubscriptionId).IsRequired();
        builder.Property(s => s.PlanHandle).IsRequired().HasMaxLength(255);
        builder.Property(s => s.State).IsRequired().HasMaxLength(50);
        builder.Property(s => s.CurrentPrice).HasPrecision(18, 2);
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();
    }
}
