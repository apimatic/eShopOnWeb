using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Claim + mapping of an eShopOnWeb user to its Maxio customer. The primary key (UserId) is what
/// refuses a second concurrent "ensure customer" before it reaches Maxio.
/// </summary>
public class MaxioCustomerLink
{
    public string UserId { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
