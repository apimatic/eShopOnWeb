using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Maps an eShopOnWeb user to its billing-system customer. The primary key (UserId) is the claim:
/// the store refuses a second insert, so a double submit can never create two customers.
/// </summary>
public class MaxioCustomerLink : IAggregateRoot
{
    public string UserId { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public LinkStatus Status { get; set; }
    public DateTime ClaimedAtUtc { get; set; }
}
