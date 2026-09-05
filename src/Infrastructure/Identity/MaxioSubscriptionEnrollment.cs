using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Coordinates a local user's enrollment in a Maxio product. Maxio is the billing system
/// of record; this row is an idempotency reservation and a durable application mapping.
/// </summary>
public class MaxioSubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public long MaxioCustomerId { get; set; }
    public long? MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
