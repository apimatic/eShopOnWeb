using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionRecordConfiguration : IEntityTypeConfiguration<SubscriptionRecord>
{
    public void Configure(EntityTypeBuilder<SubscriptionRecord> builder)
    {
        builder.ToTable("SubscriptionRecords");

        builder.Property(s => s.Id)
            .UseHiLo("subscription_record_hilo")
            .IsRequired();

        builder.Property(s => s.OwnerId)
            .IsRequired(true)
            .HasMaxLength(64);

        builder.Property(s => s.PlanHandle)
            .IsRequired(true)
            .HasMaxLength(100);

        builder.Property(s => s.MaxioCustomerId)
            .IsRequired(true);

        builder.Property(s => s.MaxioSubscriptionId)
            .IsRequired(true);

        builder.Property(s => s.CreatedOn)
            .IsRequired(true);

        // One subscription per (user, plan): the DB enforces what the API-level
        // idempotency guard promises, even across process restarts.
        builder.HasIndex(s => new { s.OwnerId, s.PlanHandle })
            .IsUnique();

        builder.HasIndex(s => s.MaxioSubscriptionId)
            .IsUnique();
    }
}