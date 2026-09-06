using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Builds the deterministic <c>reference</c> values this integration assigns to Maxio customers
/// and subscriptions.
/// </summary>
/// <remarks>
/// <para>
/// References are the integration's idempotency mechanism. Maxio enforces uniqueness on the
/// <c>reference</c> of both customers and subscriptions, so deriving one deterministically from
/// the eShopOnWeb identity means a duplicate submission is rejected by the provider rather than
/// creating - and billing - a second record. That check happens inside Maxio, so it holds across
/// concurrent requests and across application instances, with no local state to keep in sync.
/// </para>
/// <para>
/// Each reference is a readable slug plus a short hash of the full input. The slug keeps records
/// recognisable in the Maxio UI; the hash restores the uniqueness that truncation and character
/// folding would otherwise lose.
/// </para>
/// </remarks>
internal static class MaxioReference
{
    /// <summary>Prefix marking records this application owns on a shared billing site.</summary>
    private const string Prefix = "eshoponweb";

    private const int MaxSlugLength = 32;
    private const int HashHexLength = 8;

    /// <summary>
    /// Reference for the Maxio customer that represents an eShopOnWeb user. Derived from the user
    /// name, which is stable across application restarts - so the link survives even when the app
    /// runs on the in-memory database and every local identifier is regenerated.
    /// </summary>
    public static string ForCustomer(string userName)
    {
        var normalized = Normalize(userName);
        return $"{Prefix}-{Slug(normalized)}-{ShortHash(normalized)}";
    }

    /// <summary>
    /// Reference for a subscription, scoped to the owning customer and to an idempotency key.
    /// Two submissions carrying the same key resolve to the same subscription; a caller that
    /// genuinely wants an additional subscription supplies a different key.
    /// </summary>
    public static string ForSubscription(string customerReference, string idempotencyKey)
    {
        var normalized = Normalize(idempotencyKey);
        return $"{customerReference}--{Slug(normalized, 24)}-{ShortHash(normalized)}";
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static string Slug(string value, int maxLength = MaxSlugLength)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSeparator = false;

        foreach (var c in value)
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                builder.Append(c);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && builder.Length > 0)
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
        return Convert.ToHexString(hash, 0, HashHexLength / 2).ToLowerInvariant();
    }
}
