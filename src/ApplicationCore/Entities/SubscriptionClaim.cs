using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A claim taken before a billing write is sent to the billing provider. The primary key is
/// the operation's natural key, so a second concurrent attempt is refused by the store.
/// </summary>
public class SubscriptionClaim
{
    public string Key { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    private SubscriptionClaim() { }

    public SubscriptionClaim(string key)
    {
        Key = key;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
