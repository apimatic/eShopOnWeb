using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Maps an eShopOnWeb user to its Maxio customer. The row is inserted (the claim) before the
/// customer is created in Maxio; the primary key on <see cref="UserId"/> refuses a second claim.
/// </summary>
public class UserCustomerLink
{
    public string UserId { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public DateTime CreatedUtc { get; set; }
}
