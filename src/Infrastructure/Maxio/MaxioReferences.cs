using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Derives the Maxio <c>reference</c> values that tie Maxio records back to eShopOnWeb.
/// </summary>
/// <remarks>
/// References are the backbone of this integration's idempotency. Maxio enforces uniqueness on
/// both customer and subscription references, so deriving them deterministically from the
/// eShopOnWeb user means a repeated request can only ever resolve to the record the first request
/// created - eShopOnWeb itself stores no mapping, and needs none.
/// </remarks>
public class MaxioReferences
{
    /// <summary>
    /// Keeps composed references comfortably short. Longer inputs are replaced by a stable digest
    /// of the same input, so the reference remains deterministic.
    /// </summary>
    private const int MaxIdentityLength = 96;

    private readonly string _prefix;

    public MaxioReferences(string prefix)
    {
        _prefix = string.IsNullOrWhiteSpace(prefix) ? "eshoponweb" : prefix.Trim();
    }

    /// <summary>
    /// The Maxio customer reference for an eShopOnWeb user.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="Subscriber.UserName"/> rather than the Identity primary key: the
    /// user name is the identity the bearer token carries and the one that survives a restart of a
    /// host running on the in-memory database, where Identity keys are regenerated on every boot.
    /// </remarks>
    public string ForCustomer(Subscriber subscriber)
    {
        var identity = subscriber.UserName.Trim().ToLowerInvariant();
        if (identity.Length > MaxIdentityLength)
        {
            identity = Digest(identity);
        }

        return $"{_prefix}-{identity}";
    }

    /// <summary>
    /// The Maxio subscription reference for a (customer, plan) pair. Stable, so a replayed subscribe
    /// request collides on the reference instead of creating a second subscription.
    /// </summary>
    public string ForSubscription(string customerReference, string planHandle) =>
        $"{customerReference}:{planHandle}";

    /// <summary>
    /// A fresh reference for a plan the shopper is subscribing to again after an earlier
    /// subscription reached a terminal state and permanently claimed the stable reference.
    /// </summary>
    public string ForResubscription(string customerReference, string planHandle) =>
        $"{ForSubscription(customerReference, planHandle)}:{DateTime.UtcNow:yyyyMMddHHmmssfff}";

    private static string Digest(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }
}
