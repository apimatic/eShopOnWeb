using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Derives the references eShopOnWeb owns inside Maxio.
/// <para>
/// References are the backbone of the integration's idempotency: they are deterministic functions of
/// the shopper and the plan, they are enforced unique by Maxio, and they are stored in Maxio rather
/// than locally — so they survive an eShopOnWeb restart even when its database does not.
/// </para>
/// </summary>
public static class MaxioReference
{
    // Long enough to stay readable in the Maxio UI, short enough to leave room for the suffixes.
    private const int MaxSlugLength = 40;

    /// <summary>
    /// A stable, unique customer reference for a shopper, e.g.
    /// <c>eshoponweb-demouser-at-microsoft-com-3f1c9a2e</c>. The readable slug is for humans looking
    /// at the Maxio UI; the hash suffix is what guarantees distinct logins never collide after
    /// slugging has flattened their punctuation.
    /// </summary>
    public static string ForCustomer(string prefix, string stableKey)
    {
        if (string.IsNullOrWhiteSpace(stableKey))
        {
            throw new ArgumentException("A stable subscriber key is required.", nameof(stableKey));
        }

        var normalized = stableKey.Trim().ToLowerInvariant();
        return $"{Slug(prefix)}-{Truncate(Slug(normalized), MaxSlugLength)}-{ShortHash(normalized)}";
    }

    /// <summary>
    /// The reference for a shopper's subscription to a plan. Attempt 0 is the canonical reference;
    /// higher attempts are only used to start a fresh subscription after an earlier one on the same
    /// plan reached end of life.
    /// </summary>
    public static string ForSubscription(string customerReference, string planHandle, int attempt)
    {
        if (attempt < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt));
        }

        var baseReference = $"{customerReference}--{Truncate(Slug(planHandle), MaxSlugLength)}";
        return attempt == 0 ? baseReference : $"{baseReference}--r{attempt.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// A fresh uniqueness token for one subscribe attempt.
    /// <para>
    /// Its job is narrow: make a single POST safe to replay at the transport layer. If a create
    /// times out or 5xxs after Maxio already committed it, the replay carries the same token and is
    /// rejected with 409 instead of enrolling the shopper twice.
    /// </para>
    /// <para>
    /// Deliberately random rather than derived from the reference. Maxio remembers a token for 60
    /// minutes regardless of whether the request it guarded succeeded, so a token tied to the
    /// reference would lock a shopper out for an hour after a failed attempt. Exactly-once across
    /// separate attempts is guaranteed by the subscription reference being unique in Maxio, which
    /// holds forever rather than for an hour.
    /// </para>
    /// </summary>
    public static string NewUniquenessToken() => Guid.NewGuid().ToString();

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSeparator = false;

        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value.Substring(0, maxLength).TrimEnd('-');

    private static string ShortHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }
}
