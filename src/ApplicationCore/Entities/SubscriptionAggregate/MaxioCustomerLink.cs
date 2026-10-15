using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Claim + record of the Maxio customer created for an eShopOnWeb user. The primary key (UserId)
/// is the duplicate guard: a second claim for the same user is refused by the store.
/// </summary>
public class MaxioCustomerLink
{
    public string UserId { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public SubscriptionLinkState State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
