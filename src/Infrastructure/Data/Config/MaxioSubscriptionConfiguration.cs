using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionConfiguration : IEntityTypeConfiguration<MaxioSubscription>
{
    public void Configure(EntityTypeBuilder<MaxioSubscription> builder)
    {
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.PlanHandle).HasMaxLength(100).IsRequired();
        builder.Property(x => x.State).HasMaxLength(50);
        builder.Property(x => x.Currency).HasMaxLength(10);
        builder.HasIndex(x => new { x.UserId, x.PlanHandle }).IsUnique();
    }
}