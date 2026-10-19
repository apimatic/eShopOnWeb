using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Deterministic billing references derived from the eShopOnWeb user name. The same shopper always maps to
/// the same Maxio customer and subscription reference — across requests, processes and restarts — which is
/// what lets an ambiguous write be found again. The raw user name (an e-mail address) never appears in them.
/// </summary>
public static class BillingReferences
{
    private const string PREFIX = "eshop-";

    public static string ForUser(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user name is required.", nameof(userName));
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToUpperInvariant()));
        return PREFIX + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    public static string ForCustomer(string userReference) => userReference;

    public static string ForSubscription(string userReference) => userReference + "-sub";

    /// <summary>eShopOnWeb identities carry only a user name (an e-mail address); Maxio requires first and last names.</summary>
    public static (string FirstName, string LastName, string Email) CustomerNames(string userName)
    {
        var email = userName.Trim();
        var at = email.IndexOf('@');
        if (at <= 0)
        {
            throw new SubscriptionBillingException(BillingFailureKind.InvalidRequest,
                "Your account needs an e-mail address before it can subscribe.");
        }
        return (email[..at], "eShopOnWeb Shopper", email);
    }
}
