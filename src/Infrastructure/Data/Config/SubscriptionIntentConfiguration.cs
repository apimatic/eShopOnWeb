using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionIntentConfiguration : IEntityTypeConfiguration<SubscriptionIntent>
{
    public void Configure(EntityTypeBuilder<SubscriptionIntent> builder)
    {
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.ProviderReference).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PlanName).HasMaxLength(255);
        builder.Property(x => x.PlanHandle).HasMaxLength(255);
        builder.Property(x => x.ProviderState).HasMaxLength(50);

        builder.HasIndex(x => new { x.UserId, x.ProductId }).IsUnique();
        builder.HasIndex(x => x.ProviderReference).IsUnique();
    }
}
