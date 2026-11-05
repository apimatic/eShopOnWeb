using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Links an eShopOnWeb user to its Maxio customer. The row is written (without an id) before Maxio is called,
/// so its primary key (the normalized user name) is the claim that stops a second customer being created.
/// </summary>
public class MaxioCustomerLink
{
    public string UserName { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
