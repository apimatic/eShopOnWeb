using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum ClaimStatus
{
    /// <summary>Claim taken, provider call not yet settled.</summary>
    Pending = 0,
    /// <summary>Provider call settled, provider id recorded.</summary>
    Completed = 1,
    /// <summary>Connection failed after the provider may have acted; to be settled by a re-read.</summary>
    Unknown = 2
}

/// <summary>
/// Maps an eShopOnWeb user to its Maxio customer. The primary key (UserId) is the claim:
/// the store refuses a second concurrent claim for the same user.
/// </summary>
public class MaxioCustomerLink
{
    public string UserId { get; set; } = string.Empty;
    /// <summary>The customer reference sent to Maxio (equals UserId).</summary>
    public string Reference { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public ClaimStatus Status { get; set; }
    public DateTime ClaimedAtUtc { get; set; }
}
