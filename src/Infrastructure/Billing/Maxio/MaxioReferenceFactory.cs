using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Builds the references eShopOnWeb writes onto Maxio customers and subscriptions.
///
/// References are the integration's idempotency primitive. Advanced Billing enforces uniqueness on
/// both customer and subscription references, so a duplicate submit is answered with a 422 rather
/// than creating a second record - a guarantee that holds across processes and restarts, which an
/// in-process guard cannot give. Every reference is namespaced with a configurable prefix so
/// records written by eShopOnWeb are recognisable in the Maxio UI and cannot collide with records
/// written by another system on the same site.
/// </summary>
internal sealed class MaxioReferenceFactory
{
    /// <summary>
    /// Keeps a reference comfortably short. Long subscriber keys are folded into a hash suffix
    /// rather than truncated, so two long keys sharing a prefix still get distinct references.
    /// </summary>
    private const int MaxSubscriberSegmentLength = 80;

    private readonly string _prefix;

    public MaxioReferenceFactory(string prefix)
    {
        _prefix = string.IsNullOrWhiteSpace(prefix) ? "eshop" : prefix.Trim();
    }

    /// <summary>Reference for the billing customer standing in for one eShopOnWeb user.</summary>
    public string CustomerReference(string subscriberKey) =>
        $"{_prefix}:cust:{Segment(subscriberKey)}";

    /// <summary>
    /// Reference for a subscription created from a caller-supplied idempotency key. Replaying the
    /// same key always targets the same reference, so the billing system rejects the duplicate.
    /// </summary>
    public string SubscriptionReference(string subscriberKey, string idempotencyKey) =>
        $"{_prefix}:sub:{Segment(subscriberKey)}:k:{Segment(idempotencyKey)}";

    /// <summary>
    /// Reference for a subscription created without a caller-supplied key.
    /// <paramref name="sequence"/> is the number of subscriptions the subscriber already has on
    /// this plan, plus one. That makes the reference a pure function of state the caller can see,
    /// so two concurrent double-click requests derive the same reference and one of them loses the
    /// uniqueness check - while a shopper legitimately re-subscribing after a cancellation derives
    /// the next sequence number and gets a genuinely new subscription.
    /// </summary>
    public string SubscriptionReference(string subscriberKey, string planHandle, int sequence) =>
        $"{_prefix}:sub:{Segment(subscriberKey)}:{Segment(planHandle)}:{sequence}";

    private static string Segment(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length <= MaxSubscriberSegmentLength)
        {
            return normalized;
        }

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..12].ToLowerInvariant();
        return $"{normalized[..(MaxSubscriberSegmentLength - 13)]}~{digest}";
    }
}
