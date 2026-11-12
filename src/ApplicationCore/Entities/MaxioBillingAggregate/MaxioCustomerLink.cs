using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.MaxioBillingAggregate;

/// <summary>
/// Maps an eShopOnWeb user to its Maxio customer. The primary key (UserId) is the duplicate-prevention
/// claim: the row is inserted as Claimed BEFORE the provider is called.
/// </summary>
public class MaxioCustomerLink : IAggregateRoot
{
    public string UserId { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public MaxioLinkStatus Status { get; set; }
    public DateTime CreatedUtc { get; set; }
}
