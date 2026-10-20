using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public enum ClaimStatus
{
    Pending = 0,
    Completed = 1,
    Unknown = 2
}

/// <summary>
/// Claim taken BEFORE the billing provider is asked to create a subscription. The primary key
/// (<see cref="ClaimKey"/> = "{userKey}|{planHandle}") is what rejects a second concurrent or repeated request.
/// </summary>
public class SubscriptionClaim
{
    public string ClaimKey { get; set; } = string.Empty;
    public ClaimStatus Status { get; set; }
    public DateTimeOffset ClaimedAt { get; set; }
    public int? MaxioSubscriptionId { get; set; }
}
