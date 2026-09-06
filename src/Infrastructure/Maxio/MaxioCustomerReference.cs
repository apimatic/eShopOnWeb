using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Builds the stable key that links an eShopOnWeb account to its Maxio customer. Maxio allows only
/// one customer per reference value per site, which is what makes "ensure a customer exists"
/// idempotent without any local bookkeeping.
/// </summary>
public static class MaxioCustomerReference
{
    /// <summary>Namespaces our references inside a Maxio site that may be shared with other apps.</summary>
    public const string Prefix = "eshoponweb";

    /// <summary>
    /// Derived from the account email rather than the Identity user id: the email is the store's
    /// login identity and stays the same across identity-store reseeds, so a shopper keeps their
    /// Maxio customer - and their subscriptions - instead of accumulating a new one each time.
    /// </summary>
    public static string For(SubscriberIdentity subscriber) =>
        $"{Prefix}:{subscriber.Email.Trim().ToLowerInvariant()}";
}
