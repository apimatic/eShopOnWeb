using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Claim/record linking an eShopOnWeb user to its Maxio customer. The primary key (BuyerId)
/// is what rejects a second concurrent attempt to create the customer.
/// </summary>
public class MaxioCustomerLink
{
    public string BuyerId { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
