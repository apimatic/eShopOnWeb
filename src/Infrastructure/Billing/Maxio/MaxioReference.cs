using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Builds the client-supplied reference values that make the integration idempotent.
/// <para>
/// A customer reference is derived from the shopper's login name rather than from a database key,
/// deliberately: the eShopOnWeb identity store can be reseeded (and in the in-memory
/// configuration is reseeded on every restart), which would hand the same shopper a new primary
/// key and therefore a second billing customer. The login name is stable across that.
/// </para>
/// <para>
/// The readable slug exists so an operator can recognise the record in the Maxio UI; the hash
/// suffix keeps the value unique even when two different login names slugify to the same text.
/// </para>
/// </summary>
public static class MaxioReference
{
    private const string CustomerPrefix = "eshoponweb";
    private const int MaxSlugLength = 40;
    private const int MaxHandleSlugLength = 30;
    private const int HashLength = 8;

    /// <summary>Stable Maxio customer reference for an eShopOnWeb login name.</summary>
    public static string ForCustomer(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user name is required to derive a customer reference.", nameof(userName));
        }

        var normalized = userName.Trim().ToLowerInvariant();
        return CustomerPrefix + "-" + Slug(normalized, MaxSlugLength) + "-" + ShortHash(normalized);
    }

    /// <summary>
    /// Deterministic subscription reference for a (customer, plan) pair. <paramref name="attempt"/>
    /// is 1 for the shopper's first subscription to that plan and increments only when an earlier,
    /// now-ended subscription already owns the reference - so re-subscribing after a cancellation
    /// stays possible without ever colliding.
    /// </summary>
    public static string ForSubscription(string customerReference, string planHandle, int attempt = 1)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(customerReference));
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var reference = customerReference + "--" + Slug(planHandle.Trim().ToLowerInvariant(), MaxHandleSlugLength);
        return attempt <= 1
            ? reference
            : reference + "--" + attempt.ToString(CultureInfo.InvariantCulture);
    }

    private static string Slug(string value, int maxLength)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSeparator = true;

        foreach (var character in value)
        {
            if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
            {
                builder.Append(character);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }

            if (builder.Length >= maxLength)
            {
                break;
            }
        }

        return builder.ToString().Trim('-') is { Length: > 0 } slug ? slug : "user";
    }

    private static string ShortHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).Substring(0, HashLength).ToLowerInvariant();
    }
}
