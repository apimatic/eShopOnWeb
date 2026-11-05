using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.MaxioSubscriptions;

/// <summary>
/// One subscription per user per plan. The composite primary key (UserId, ProductHandle) is the claim.
/// </summary>
public class MaxioSubscriptionClaim
{
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public int? MaxioSubscriptionId { get; set; }
    /// <summary>Deterministic Maxio subscription reference; used to reconcile after an unknown outcome.</summary>
    public string Reference { get; set; } = string.Empty;
    public DateTime ClaimedAtUtc { get; set; }
    public MaxioClaimStatus Status { get; set; }
}
