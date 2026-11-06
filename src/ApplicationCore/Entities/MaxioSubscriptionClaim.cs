using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public enum MaxioSubscriptionClaimStatus
{
    /// <summary>Claimed; the Maxio call is in flight or its outcome is unknown.</summary>
    Pending = 0,
    Completed = 1
}

/// <summary>
/// One subscription per user per plan. The composite primary key (UserId, ProductHandle) is the claim
/// that stops a double-click or concurrent request from reaching Maxio twice.
/// </summary>
public class MaxioSubscriptionClaim
{
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Deterministic Maxio subscription reference derived from user and plan.</summary>
    public string Reference { get; set; } = string.Empty;

    public int? MaxioSubscriptionId { get; set; }
    public MaxioSubscriptionClaimStatus Status { get; set; } = MaxioSubscriptionClaimStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
