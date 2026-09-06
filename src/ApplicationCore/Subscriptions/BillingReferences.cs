using System;
using System.Security.Cryptography;
using System.Text;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Builds the deterministic <c>reference</c> values this integration stamps onto billing-provider
/// records.
/// <para>
/// These references are the backbone of idempotency. The provider enforces per-site uniqueness on
/// customer and subscription references, so deriving them deterministically from data we always
/// have (the login name, the plan handle) turns "create" into "create-or-adopt" without needing a
/// local mapping table. That matters here because the reference implementation may run against an
/// in-memory database that loses everything on restart.
/// </para>
/// </summary>
public static class BillingReferences
{
    private const string Prefix = "eshop";
    private const int MaxSlugLength = 48;
    private const int HashLength = 8;

    /// <summary>
    /// Reference for the billing customer that represents an eShopOnWeb user.
    /// Shape: <c>eshop-{slug}-{hash}</c>, e.g. <c>eshop-demouser-microsoft-com-3f2a9c11</c>.
    /// </summary>
    /// <remarks>
    /// The slug exists only so the record is recognisable in the billing UI; the trailing hash of
    /// the untruncated, case-normalised user name is what guarantees two different logins can never
    /// collapse onto the same reference.
    /// </remarks>
    public static string ForUser(string userName)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var normalized = userName.Trim().ToLowerInvariant();
        return $"{Prefix}-{Slugify(normalized, MaxSlugLength)}-{ShortHash(normalized)}";
    }

    /// <summary>
    /// Reference for a subscription of <paramref name="planHandle"/> owned by
    /// <paramref name="customerReference"/>.
    /// </summary>
    /// <param name="attempt">
    /// Zero-based ordinal of this subscription among the customer's subscriptions to the same plan.
    /// It is computed from live provider state, so concurrent callers derive the same value and the
    /// provider's uniqueness constraint settles the race; a shopper who cancels and later resubscribes
    /// gets the next ordinal instead of colliding with their retired subscription.
    /// </param>
    public static string ForSubscription(string customerReference, string planHandle, int attempt)
    {
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Guard.Against.Negative(attempt, nameof(attempt));

        var suffix = attempt == 0 ? string.Empty : $"-{attempt + 1}";
        return $"{customerReference}-{Slugify(planHandle.ToLowerInvariant(), MaxSlugLength)}{suffix}";
    }

    /// <summary>
    /// Reference for a subscription whose idempotency is pinned by a caller-supplied key. Two requests
    /// carrying the same key can never produce two subscriptions, regardless of provider state.
    /// </summary>
    public static string ForSubscription(string customerReference, string planHandle, string idempotencyKey)
    {
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));

        return $"{customerReference}-{Slugify(planHandle.ToLowerInvariant(), MaxSlugLength)}-{ShortHash(idempotencyKey.Trim())}";
    }

    private static string Slugify(string value, int maxLength)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSeparator = true;

        foreach (var c in value)
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                builder.Append(c);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > maxLength)
        {
            slug = slug.Substring(0, maxLength).TrimEnd('-');
        }

        return slug.Length == 0 ? "user" : slug;
    }

    private static string ShortHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, HashLength / 2).ToLowerInvariant();
    }
}
