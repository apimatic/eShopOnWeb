using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Maps an eShopOnWeb user to the Maxio customer that represents them. The row's primary key (the user id)
/// doubles as the claim that stops two concurrent requests from creating two Maxio customers.
/// </summary>
public class MaxioCustomerLink
{
    public string UserId { get; set; } = string.Empty;

    /// <summary>The Maxio customer <c>reference</c> sent on create; the lookup key used to settle unknown outcomes.</summary>
    public string CustomerReference { get; set; } = string.Empty;

    /// <summary>Null until the Maxio customer is known to exist.</summary>
    public int? MaxioCustomerId { get; set; }

    public DateTimeOffset ClaimedAt { get; set; } = DateTimeOffset.UtcNow;
}
