using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Derives the stable, provider-side identity of an eShopOnWeb shopper.
/// <para>
/// The reference is what makes "ensure a customer exists" idempotent: Maxio guarantees at most one
/// customer per reference value, so two concurrent creates with the same reference cannot both succeed.
/// It is therefore derived deterministically from the shopper's e-mail — the one identifier that is
/// stable across host restarts, including under the in-memory identity store, which regenerates user ids.
/// </para>
/// </summary>
internal static class MaxioCustomerReference
{
    private const string Prefix = "eshoponweb";
    private const int MaxSlugLength = 40;

    private static readonly Regex NonSlugCharacters = new("[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Builds the customer reference for <paramref name="email"/>. The readable slug keeps the record
    /// recognisable in the Maxio UI; the hash suffix keeps two different addresses that slugify
    /// identically (<c>a.b@x.com</c> and <c>a-b@x.com</c>) from colliding onto one customer.
    /// </summary>
    public static string ForEmail(string email)
    {
        var normalized = Normalize(email);

        var slug = NonSlugCharacters.Replace(normalized, "-").Trim('-');
        if (slug.Length > MaxSlugLength)
        {
            slug = slug.Substring(0, MaxSlugLength).TrimEnd('-');
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var suffix = Convert.ToHexString(digest, 0, 4).ToLowerInvariant();

        return slug.Length == 0 ? $"{Prefix}-{suffix}" : $"{Prefix}-{slug}-{suffix}";
    }

    /// <summary>
    /// Maxio requires a first and last name on a customer, but eShopOnWeb identity carries neither.
    /// Derive something recognisable from the address rather than sending a placeholder.
    /// </summary>
    public static (string FirstName, string LastName) NamesForEmail(string email)
    {
        var localPart = Normalize(email).Split('@')[0];
        var parts = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part.Length > 0)
            .ToArray();

        if (parts.Length == 0)
        {
            return ("eShopOnWeb", "Subscriber");
        }

        var first = TitleCase(parts[0]);
        var last = parts.Length > 1 ? string.Join(" ", parts.Skip(1).Select(TitleCase)) : "Subscriber";
        return (first, last);
    }

    private static string Normalize(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string TitleCase(string value) =>
        value.Length == 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value.Substring(1);
}
