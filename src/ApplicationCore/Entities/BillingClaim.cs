using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A claim taken before a billing write is sent to the billing provider. The primary key (Key)
/// makes the store refuse a second concurrent claim for the same operation.
/// </summary>
public class BillingClaim
{
    public string Key { get; set; } = string.Empty;
    public DateTime ClaimedAtUtc { get; set; }
}
