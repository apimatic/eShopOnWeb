using System;
using System.Globalization;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Builds the <c>reference</c> values this application writes to Maxio. A reference is unique per
/// site, so a deterministic reference turns "create" into an idempotent operation: the second
/// attempt is rejected by Maxio itself rather than by an optimistic check that can lose a race.
/// </summary>
internal static class MaxioReference
{
    /// <summary>
    /// Defensive cap on reference length - the specification does not state one, so this keeps a
    /// pathologically long login from producing an unusable reference.
    /// </summary>
    private const int MaxLength = 255;

    /// <summary>
    /// Reference for the Maxio customer that represents an eShopOnWeb shopper, e.g.
    /// <c>eshop:demouser@microsoft.com</c>. Derived from the login name because that is the shopper
    /// identity that survives an application restart.
    /// </summary>
    public static string ForCustomer(string prefix, string userName) =>
        Fit($"{Normalize(prefix)}:{Normalize(userName)}");

    /// <summary>
    /// Reference for a subscription. <paramref name="attempt"/> 1 produces the natural slot; higher
    /// attempts are only used when the previous slots are occupied by subscriptions that have already
    /// ended, which happens when a shopper re-subscribes to a plan they once held.
    /// </summary>
    public static string ForSubscription(string customerReference, string scope, int attempt)
    {
        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "Reference attempts start at 1.");
        }

        var suffix = attempt == 1 ? string.Empty : $"#{attempt.ToString(CultureInfo.InvariantCulture)}";
        return Fit($"{customerReference}:{Normalize(scope)}{suffix}");
    }

    /// <summary>
    /// The per-subscription scope: the caller's idempotency key when supplied, otherwise the plan
    /// handle - so a double submit of "subscribe to Pro" resolves to one subscription, while
    /// subscribing to a different plan is a genuinely different request.
    /// </summary>
    public static string ScopeFor(string planHandle, string? idempotencyKey) =>
        string.IsNullOrWhiteSpace(idempotencyKey) ? planHandle : $"key:{idempotencyKey}";

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    /// <summary>
    /// Keeps references inside <see cref="MaxLength"/> without losing uniqueness: an over-long value
    /// is truncated and stamped with a hash of the whole original.
    /// </summary>
    private static string Fit(string reference)
    {
        if (reference.Length <= MaxLength)
        {
            return reference;
        }

        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(reference)))[..16].ToLowerInvariant();

        return string.Concat(reference.AsSpan(0, MaxLength - hash.Length - 1), "~", hash);
    }
}
