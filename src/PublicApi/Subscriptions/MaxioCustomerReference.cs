using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Derives the stable Maxio customer identifier (the customer "reference" attribute) and a
/// minimal customer profile from an eShopOnWeb identity. Maxio is the system of record: the
/// reference is what joins an eShopOnWeb shopper to their Maxio customer/subscriptions, so it
/// must be a pure, deterministic function of the authenticated user name.
/// </summary>
public static class MaxioCustomerReference
{
    public const string Prefix = "eshop:";

    /// <summary>Maxio customer reference for an eShopOnWeb user name.</summary>
    public static string Create(string userName)
    {
        var normalized = Normalize(userName);
        return $"{Prefix}{normalized}";
    }

    /// <summary>Canonical (lower-cased, trimmed) user name used as the join key.</summary>
    public static string Normalize(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user name is required to identify the Maxio customer.", nameof(userName));
        }

        return userName.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Builds the customer profile sent to Maxio on first enrollment. eShopOnWeb identities do
    /// not carry first/last names (UserName == Email), so a best-effort split of the email
    /// local-part/domain is used.
    /// </summary>
    public static MaxioCustomerProfile Profile(string userName)
    {
        var email = Normalize(userName);
        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1)
        {
            return new MaxioCustomerProfile(email, email, "User");
        }

        var local = email.Substring(0, at);
        var domain = email.Substring(at + 1);

        var labels = domain.Split('.');
        string lastName = labels.Length switch
        {
            1 => domain,
            _ => string.Join(".", labels, 0, labels.Length - 1)
        };

        return new MaxioCustomerProfile(email, local, lastName);
    }
}

/// <summary>Profile data used when creating a Maxio customer.</summary>
public readonly record struct MaxioCustomerProfile(string Email, string FirstName, string LastName);
