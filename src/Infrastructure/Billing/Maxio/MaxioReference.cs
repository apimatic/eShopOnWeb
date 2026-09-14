using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Builds the application-supplied <c>reference</c> values that anchor this integration's records in
/// Advanced Billing.
/// </summary>
/// <remarks>
/// References are deterministic, which is what makes enrollment idempotent: Advanced Billing enforces
/// uniqueness of both the customer reference and the subscription reference per site, so two requests
/// racing on the same eShopOnWeb user and plan can only ever produce one record — the loser is
/// rejected with a duplicate-reference error and reads back the winner.
/// <para>
/// A reference is a readable slug of the source value plus a short digest of it. The slug keeps the
/// value recognisable in the Advanced Billing UI; the digest guarantees two different user keys can
/// never collapse onto the same reference after slugging.
/// </para>
/// </remarks>
internal static class MaxioReference
{
    private const int MaxSlugLength = 48;
    private const int DigestLength = 10;

    /// <summary>Separates the parts of a subscription reference; single dashes occur inside slugs.</summary>
    private const string PartSeparator = "--";

    /// <summary>
    /// Reference of the Advanced Billing customer that stands for an eShopOnWeb user.
    /// </summary>
    public static string ForCustomer(string prefix, string userKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userKey);

        var normalizedPrefix = Slugify(prefix);
        var body = $"{Slugify(userKey)}-{Digest(userKey.Trim().ToLowerInvariant())}";

        return string.IsNullOrEmpty(normalizedPrefix) ? body : $"{normalizedPrefix}-{body}";
    }

    /// <summary>
    /// Reference of a subscription. <paramref name="attempt"/> is 1 for the shopper's first
    /// enrollment in the plan and is incremented only when an earlier, now finished subscription
    /// already occupies the reference.
    /// </summary>
    public static string ForSubscription(string customerReference, string planHandle, int attempt = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(planHandle);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        var reference = $"{customerReference}{PartSeparator}{Slugify(planHandle)}";

        return attempt == 1
            ? reference
            : $"{reference}{PartSeparator}{attempt.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Lower-cases the value and collapses everything that is not a letter or digit into single dashes.</summary>
    private static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasDash = false;

        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                previousWasDash = false;
            }
            else if (!previousWasDash && builder.Length > 0)
            {
                builder.Append('-');
                previousWasDash = true;
            }

            if (builder.Length >= MaxSlugLength)
            {
                break;
            }
        }

        return builder.ToString().Trim('-');
    }

    /// <summary>Short, stable, lower-case hex digest used to keep slugged references collision free.</summary>
    private static string Digest(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..DigestLength].ToLowerInvariant();
    }
}
