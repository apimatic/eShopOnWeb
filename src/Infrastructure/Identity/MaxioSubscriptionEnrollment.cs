using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Records the subscription created in Maxio for an application user and plan.
/// The unique pair is also an idempotency boundary for subscription enrollment.
/// </summary>
public class MaxioSubscriptionEnrollment
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string ProductHandle { get; set; } = string.Empty;

    public long? MaxioSubscriptionId { get; set; }

    /// <summary>Lease for the process currently making the external enrollment request.</summary>
    public DateTimeOffset? ProvisioningStartedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
