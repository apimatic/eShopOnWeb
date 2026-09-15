using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class MaxioSubscriptionRecordConfiguration : IEntityTypeConfiguration<MaxioSubscriptionRecord>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionRecord> builder)
    {
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.PlanHandle).IsRequired().HasMaxLength(255);
        builder.Property(x => x.SubscriptionReference).IsRequired().HasMaxLength(255);
        builder.HasIndex(x => new { x.UserId, x.PlanHandle }).IsUnique();
        builder.HasIndex(x => x.SubscriptionReference).IsUnique();
    }
}
