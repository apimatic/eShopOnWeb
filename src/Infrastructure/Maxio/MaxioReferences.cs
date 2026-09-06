using System;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Builds the reference values eShopOnWeb writes into Maxio.
/// <para>
/// References are the integration's idempotency mechanism: Maxio enforces uniqueness on both the
/// customer reference and the subscription reference, so a deterministic reference turns a repeated
/// write into a 422 we can recognise and resolve by reading the record that already exists. They are
/// derived from the user name in the bearer token, which is stable across restarts - unlike the
/// in-memory identity store's row ids.
/// </para>
/// </summary>
public static class MaxioReferences
{
    /// <summary>Defensive cap on caller-supplied idempotency keys folded into a reference.</summary>
    public const int MaxIdempotencyKeyLength = 64;

    /// <summary>
    /// Reference for the Maxio customer that represents an eShopOnWeb user, for example
    /// <c>eshoponweb:demouser@microsoft.com</c>.
    /// </summary>
    public static string ForCustomer(string prefix, string userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) throw new ArgumentException("User name is required.", nameof(userName));

        return $"{Normalize(prefix)}:{userName.Trim().ToLowerInvariant()}";
    }

    /// <summary>
    /// Reference for a subscription created without a caller-supplied idempotency key.
    /// <paramref name="attempt"/> 1 yields the bare "<c>{customer}:{plan}</c>" form; later attempts
    /// append an ordinal so a shopper can re-subscribe to a plan they previously cancelled.
    /// </summary>
    public static string ForSubscription(string customerReference, string planHandle, int attempt)
    {
        if (attempt < 1) throw new ArgumentOutOfRangeException(nameof(attempt));

        var reference = $"{customerReference}:{planHandle.Trim().ToLowerInvariant()}";

        return attempt == 1
            ? reference
            : $"{reference}:{attempt.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Reference for a subscription created under a caller-supplied idempotency key. The plan is
    /// deliberately not part of it: an idempotency key identifies the request, so replaying it must
    /// return the original subscription rather than create a second one.
    /// </summary>
    public static string ForSubscription(string customerReference, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));

        var key = idempotencyKey.Trim();
        if (key.Length > MaxIdempotencyKeyLength)
        {
            key = key[..MaxIdempotencyKeyLength];
        }

        return $"{customerReference}:key:{key}";
    }

    private static string Normalize(string prefix) =>
        string.IsNullOrWhiteSpace(prefix) ? "eshoponweb" : prefix.Trim().ToLowerInvariant();
}
