using System;
using System.Globalization;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// Builds the <c>reference</c> values eShopOnWeb writes into Maxio.
///
/// Maxio enforces uniqueness on both the customer and the subscription <c>reference</c> (a duplicate is
/// rejected with HTTP 422), so a deterministic reference is the integration's idempotency key: it makes
/// Maxio itself the arbiter of "this shopper already has this", with no local mapping to keep in sync.
/// </summary>
internal static class MaxioReference
{
    /// <summary>Maxio rejects references longer than this.</summary>
    private const int MaxLength = 255;

    /// <summary>Stable reference for the Maxio customer that represents an eShopOnWeb user.</summary>
    public static string ForCustomer(string prefix, string userKey) =>
        Truncate($"{Normalize(prefix)}:customer:{Normalize(userKey)}");

    /// <summary>
    /// Stable reference for a subscription. <paramref name="idempotencyKey"/> is the caller's replay key
    /// when supplied, otherwise the plan handle, which is what makes a double-clicked "Subscribe" collide.
    /// </summary>
    public static string ForSubscription(string prefix, string userKey, string idempotencyKey, int attempt = 1)
    {
        var reference = $"{Normalize(prefix)}:subscription:{Normalize(userKey)}:{Normalize(idempotencyKey)}";

        if (attempt > 1)
        {
            reference += "#" + attempt.ToString(CultureInfo.InvariantCulture);
        }

        return Truncate(reference);
    }

    /// <summary>
    /// Lower-cases and strips characters that would make a reference ambiguous or awkward to round-trip
    /// through a URL query string, while keeping it recognisable to a human reading it in Maxio.
    /// </summary>
    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Reference segments cannot be empty.", nameof(value));
        }

        var builder = new StringBuilder(value.Length);

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or '@' or '+')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('-');
            }
        }

        return builder.ToString();
    }

    private static string Truncate(string reference) =>
        reference.Length <= MaxLength ? reference : reference.Substring(0, MaxLength);
}
