using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Builds the <c>reference</c> values this application assigns to Maxio customers and subscriptions.
/// </summary>
/// <remarks>
/// References are the integration's idempotency anchor: they are derived deterministically from the
/// eShopOnWeb user, so the same user always resolves to the same Maxio customer, and the same
/// user/plan pair always resolves to the same subscription. Because Maxio holds the mapping, no
/// local table is needed and nothing is lost when this application restarts.
/// </remarks>
public static class MaxioReference
{
    /// <summary>Conservative cap so a long email can never produce a rejected reference.</summary>
    private const int MaxLength = 100;

    /// <summary>The reference for the Maxio customer that mirrors an eShopOnWeb user.</summary>
    public static string ForCustomer(string prefix, string userKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userKey);

        return Compose(Slug(prefix), Slug(userKey));
    }

    /// <summary>
    /// The reference for a subscription of <paramref name="customerReference"/> to
    /// <paramref name="planHandle"/>. <paramref name="attempt"/> starts at 1; higher values produce
    /// distinct references, which is how a shopper can re-subscribe after an earlier subscription
    /// to the same plan was canceled or expired.
    /// </summary>
    public static string ForSubscription(string customerReference, string planHandle, int attempt = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(planHandle);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        var suffix = attempt == 1 ? string.Empty : "-" + attempt.ToString(CultureInfo.InvariantCulture);
        return Compose(customerReference, Slug(planHandle) + suffix);
    }

    private static string Compose(string left, string right)
    {
        var candidate = string.IsNullOrEmpty(left) ? right : $"{left}-{right}";

        // Keep the readable head and disambiguate with a hash of the full value when it is too long.
        if (candidate.Length <= MaxLength)
        {
            return candidate;
        }

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidate)))[..12].ToLowerInvariant();
        return candidate[..(MaxLength - digest.Length - 1)].TrimEnd('-') + "-" + digest;
    }

    /// <summary>Reduces arbitrary text to lowercase <c>[a-z0-9-]</c> so references stay URL and log friendly.</summary>
    private static string Slug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }
}
