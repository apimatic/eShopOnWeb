using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A claim taken before a billing write is sent to Maxio. The primary key refuses a second concurrent
/// claim for the same operation, so the second caller never reaches the provider.
/// </summary>
public class BillingClaim : IAggregateRoot
{
    public string Key { get; private set; }
    public System.DateTimeOffset ClaimedAt { get; private set; }

    public BillingClaim(string key)
    {
        Key = key;
        ClaimedAt = System.DateTimeOffset.UtcNow;
    }
}
