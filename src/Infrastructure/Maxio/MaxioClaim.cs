using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A claim on one write to the billing system (a customer or a subscription). The deterministic
/// <see cref="Reference"/> is the primary key, so the store refuses a second claim for the same write.
/// </summary>
public class MaxioClaim
{
    public const string KIND_CUSTOMER = "customer";
    public const string KIND_SUBSCRIPTION = "subscription";

    public const string STATE_PENDING = "Pending";
    public const string STATE_COMPLETED = "Completed";
    public const string STATE_UNKNOWN = "Unknown";

    public string Reference { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string State { get; set; } = STATE_PENDING;
    public int? MaxioId { get; set; }
    public DateTimeOffset ClaimedAt { get; set; }
}

public class MaxioClaimConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<MaxioClaim>
{
    public void Configure(EntityTypeBuilder<MaxioClaim> builder)
    {
        builder.ToTable("MaxioClaims");
        builder.HasKey(c => c.Reference);
        builder.Property(c => c.Reference).HasMaxLength(100).ValueGeneratedNever();
        builder.Property(c => c.Kind).HasMaxLength(20).IsRequired();
        builder.Property(c => c.PlanHandle).HasMaxLength(200);
        builder.Property(c => c.State).HasMaxLength(20).IsRequired();
    }
}
