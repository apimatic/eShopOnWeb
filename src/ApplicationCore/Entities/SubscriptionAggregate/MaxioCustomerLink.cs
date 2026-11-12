using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Maps an eShopOnWeb user to its Maxio customer. The primary key (UserId) doubles as the
/// duplicate-prevention claim: the row is inserted BEFORE the Maxio call is made.
/// </summary>
public class MaxioCustomerLink
{
    public string UserId { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    /// <summary>Null while the claim is in flight / outcome unknown.</summary>
    public int? MaxioCustomerId { get; set; }
    public DateTimeOffset ClaimedAt { get; set; }
}
