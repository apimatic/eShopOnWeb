using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionRecordConfiguration : IEntityTypeConfiguration<MaxioSubscriptionRecord>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionRecord> builder)
    {
        builder.Property(x => x.BuyerId)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.MaxioReference)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(x => x.PlanHandle)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.PlanName)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.State)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.IntervalUnit)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(x => x.MaxioSubscriptionId)
            .IsUnique();

        builder.HasIndex(x => x.BuyerId);
    }
}
