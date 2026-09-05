using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// A durable record of an eShop user enrolling in a Maxio plan. Maxio remains the
/// subscription system of record; this table only preserves the application-to-Maxio
/// identity and idempotency boundary.
/// </summary>
public class MaxioSubscriptionEnrollment
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string CustomerReference { get; set; } = string.Empty;

    public string SubscriptionReference { get; set; } = string.Empty;

    public string UniquenessToken { get; set; } = string.Empty;

    public int? MaxioCustomerId { get; set; }

    public int? MaxioSubscriptionId { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? AttemptedAtUtc { get; set; }
}
