using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionRecordConfiguration : IEntityTypeConfiguration<SubscriptionRecord>
{
    public void Configure(EntityTypeBuilder<SubscriptionRecord> builder)
    {
        builder.ToTable("SubscriptionRecords");

        builder.Property(s => s.BuyerId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(s => s.PlanHandle)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.PlanName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.State)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(s => new { s.BuyerId, s.PlanHandle })
            .IsUnique();

        builder.HasIndex(s => s.BillingSubscriptionId)
            .IsUnique();
    }
}
