using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Claim on "ensure a Maxio customer exists for this user". The primary key (UserKey) is what rejects a second claimant.
/// </summary>
public class MaxioCustomerClaim
{
    public string UserKey { get; set; } = string.Empty;
    public ClaimStatus Status { get; set; } = ClaimStatus.Pending;
    public int? MaxioCustomerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
