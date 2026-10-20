using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum ClaimStatus
{
    /// <summary>Claim taken; the billing call is in flight.</summary>
    Pending = 0,
    /// <summary>Billing call completed and its result is recorded.</summary>
    Ready = 1,
    /// <summary>The billing call may or may not have landed; must be settled by a re-read.</summary>
    Unknown = 2
}

/// <summary>
/// Links an eShopOnWeb user to its billing customer. The primary key (UserKey) is the claim:
/// a second concurrent claim for the same user is refused by the store.
/// </summary>
public class MaxioCustomerLink
{
    public string UserKey { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public ClaimStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
