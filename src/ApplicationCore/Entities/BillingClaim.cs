using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A claim an application instance makes before it calls the billing provider for a write that must not
/// run twice concurrently. The primary key is the claim key, so the store refuses a second claim.
/// Rows whose key starts with "customer:" also record the billing-provider customer id of a shop user.
/// </summary>
public class BillingClaim
{
    public string Key { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
