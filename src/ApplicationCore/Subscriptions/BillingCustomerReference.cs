using System;
using System.Text;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Builds the stable, eShopOnWeb-owned key that links a shopper to their customer record in the
/// billing system.
/// </summary>
/// <remarks>
/// The reference is derived from the user name rather than the Identity row id on purpose: it has
/// to survive the app being restarted against the in-memory database, where row ids are new every
/// run, and it is the only durable link back to the billing customer.
/// </remarks>
public static class BillingCustomerReference
{
    /// <summary>Namespace prefix so eShopOnWeb's customers are recognisable on a shared Maxio site.</summary>
    public const string Prefix = "eshoponweb-";

    public static string For(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user name is required to build a billing customer reference.", nameof(userName));
        }

        var normalized = userName.Trim().ToLowerInvariant();
        var builder = new StringBuilder(Prefix.Length + normalized.Length);
        builder.Append(Prefix);

        // Keep the reference readable and URL-safe: anything outside the allow-list becomes '-'.
        foreach (var character in normalized)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or '@'
                ? character
                : '-');
        }

        return builder.ToString();
    }
}
