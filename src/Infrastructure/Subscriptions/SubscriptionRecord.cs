using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

/// <summary>
/// Local record linking an eShopOnWeb user to a Maxio subscription.
/// Maxio remains the billing system of record; this row only caches the
/// mapping so we can look up subscriptions per user and make enrollment
/// idempotent.
/// </summary>
public class SubscriptionRecord
{
    public int Id { get; set; }

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string UserEmail { get; set; } = string.Empty;

    public int MaxioCustomerId { get; set; }

    public int MaxioSubscriptionId { get; set; }

    [MaxLength(128)]
    public string PlanHandle { get; set; } = string.Empty;

    [MaxLength(256)]
    public string PlanName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string State { get; set; } = string.Empty;

    public long PriceInCents { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
