using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Builds the deterministic <c>reference</c> values that make this integration idempotent.
/// </summary>
/// <remarks>
/// <para>
/// Maxio enforces uniqueness of the customer and subscription <c>reference</c> a caller supplies and
/// exposes lookup-by-reference on both, which makes the reference the natural idempotency key. Since
/// every reference is a pure function of the authenticated user (and the plan), the integration needs
/// no local userId-to-subscription table at all: the mapping lives in Maxio and survives a restart of
/// this application, including one backed by the in-memory database.
/// </para>
/// <para>
/// The user name, not the ASP.NET Identity row id, is the key. Identity ids are regenerated whenever
/// the identity store is rebuilt, which would orphan every customer created against the old ids.
/// </para>
/// </remarks>
public static class MaxioReferenceFactory
{
    /// <summary>Namespace prefix so references created here are recognisable in the Maxio UI.</summary>
    public const string Prefix = "eshoponweb";

    /// <summary>
    /// Longest reference this factory emits. Maxio accepts comfortably more, but a bound keeps a
    /// pathological user name from producing an unbounded key.
    /// </summary>
    private const int MaxReferenceLength = 200;

    /// <summary>The reference identifying the Maxio customer that mirrors an eShopOnWeb user.</summary>
    public static string ForCustomer(SubscriberIdentity subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        return Bound($"{Prefix}:{subscriber.UserName.ToLowerInvariant()}");
    }

    /// <summary>
    /// The reference identifying one enrollment. Two subscribe calls with the same subscriber, plan
    /// and idempotency key produce the same reference, so the second one finds the first's
    /// subscription instead of creating another.
    /// </summary>
    public static string ForSubscription(SubscriberIdentity subscriber, string planHandle, string? idempotencyKey = null)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentException.ThrowIfNullOrWhiteSpace(planHandle);

        var scope = string.IsNullOrWhiteSpace(idempotencyKey)
            ? planHandle.Trim().ToLowerInvariant()
            : $"{planHandle.Trim().ToLowerInvariant()}:{idempotencyKey.Trim()}";

        return Bound($"{ForCustomer(subscriber)}:{scope}");
    }

    /// <summary>
    /// Keeps a reference within <see cref="MaxReferenceLength"/> without losing determinism or
    /// uniqueness: an over-long value keeps a readable head and ends in a hash of the whole thing.
    /// </summary>
    private static string Bound(string reference)
    {
        if (reference.Length <= MaxReferenceLength)
        {
            return reference;
        }

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reference)))[..32].ToLowerInvariant();
        return string.Concat(reference.AsSpan(0, MaxReferenceLength - digest.Length - 1), "~", digest);
    }
}
