using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Builds the <c>reference</c> values this application stamps on Maxio customers and
/// subscriptions.
/// <para>
/// References are the backbone of the integration's idempotency. Maxio enforces uniqueness on
/// them per site, so a deterministic reference turns "create" into "create once": a duplicate
/// request is rejected by Maxio with a 422 that the caller can resolve to the record that already
/// exists. They are also what removes the need for a local userId-to-subscription table — the
/// mapping lives in Maxio and survives a restart of this application.
/// </para>
/// </summary>
public static class MaxioReference
{
    private const string CustomerPrefix = "eshop";
    private const string SubscriptionPrefix = "sub";
    private const int MaxSlugLength = 24;
    private const int HashLength = 8;

    /// <summary>
    /// Reference for the Maxio customer that stands for an eShopOnWeb user. Derived from the user
    /// name so it is stable for the life of the account and identical on every instance.
    /// </summary>
    public static string ForCustomer(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user name is required to derive a customer reference.", nameof(userName));
        }

        var normalized = userName.Trim().ToLowerInvariant();

        // The slug keeps the reference legible in the Maxio UI; the hash keeps it unambiguous,
        // because slugging is lossy (a@b.com and a-b.com slug identically).
        return $"{CustomerPrefix}-{Slug(normalized, MaxSlugLength)}-{ShortHash(normalized)}";
    }

    /// <summary>
    /// Reference for a subscription. <paramref name="idempotencyKey"/> is the caller's key when
    /// they supplied one, or the plan handle when they did not — either way the same logical
    /// request always produces the same reference.
    /// </summary>
    public static string ForSubscription(string customerReference, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(customerReference));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var normalized = idempotencyKey.Trim().ToLowerInvariant();
        var slug = Slug(normalized, MaxSlugLength);

        // Slugging an opaque caller key can collapse distinct keys onto each other, so an
        // unrecognisable key contributes its hash instead of a misleading slug.
        var discriminator = slug.Length == 0 || !string.Equals(slug, normalized, StringComparison.Ordinal)
            ? $"{slug}{(slug.Length == 0 ? string.Empty : "-")}{ShortHash(normalized)}"
            : slug;

        return $"{SubscriptionPrefix}-{customerReference}-{discriminator}";
    }

    /// <summary>
    /// Reference for a deliberate re-subscribe after a previous subscription to the same plan
    /// ended. Deterministic in the id of the subscription being replaced, so re-subscribing twice
    /// by accident still collapses to one new subscription.
    /// </summary>
    public static string ForResubscribe(string subscriptionReference, long previousSubscriptionId) =>
        $"{subscriptionReference}-r{previousSubscriptionId.ToString(CultureInfo.InvariantCulture)}";

    private static string Slug(string value, int maxLength)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSeparator = true;

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) && character < 128)
            {
                builder.Append(char.ToLowerInvariant(character));
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

        return builder.ToString().Trim('-');
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..HashLength];
    }
}
