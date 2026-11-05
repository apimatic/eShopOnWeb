using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.MaxioSubscriptions;

public enum MaxioClaimStatus
{
    /// <summary>Claim taken; the Maxio write is in flight or its outcome is not yet known.</summary>
    Pending,
    /// <summary>The Maxio write is confirmed.</summary>
    Ready
}

/// <summary>
/// Links one eShop identity user to one Maxio customer. The primary key (UserId) is the claim:
/// a second concurrent insert for the same user is refused by the store.
/// </summary>
public class MaxioCustomerLink
{
    public string UserId { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    /// <summary>Deterministic Maxio customer reference; used to reconcile after an unknown outcome.</summary>
    public string Reference { get; set; } = string.Empty;
    public DateTime ClaimedAtUtc { get; set; }
    public MaxioClaimStatus Status { get; set; }
}
