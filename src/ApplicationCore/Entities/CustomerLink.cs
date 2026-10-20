using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Link between an eShop user (keyed by the normalized token name) and the billing provider customer.
/// The primary key (<see cref="UserKey"/>) is what rejects a second concurrent customer creation.
/// </summary>
public class CustomerLink
{
    public string UserKey { get; set; } = string.Empty;
    public ClaimStatus Status { get; set; }
    public DateTimeOffset ClaimedAt { get; set; }
    public int? MaxioCustomerId { get; set; }
}
