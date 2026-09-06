using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Builds the reference values that tie Advanced Billing records back to eShopOnWeb shoppers.
/// </summary>
/// <remarks>
/// <para>
/// References are the integration's idempotency mechanism. Advanced Billing enforces uniqueness on both
/// <c>customer.reference</c> and <c>subscription.reference</c>, rejecting a duplicate with
/// <c>422 "Reference: must be unique"</c>. Deriving them deterministically therefore makes the billing
/// system itself the arbiter of "has this already happened?", which survives races that no amount of
/// in-process locking would — including two app instances handling the same double-click.
/// </para>
/// <para>
/// The shopper's email is the key rather than their Identity user id: it is the shopper's stable business
/// identity, and it keeps the mapping intact across restarts even when eShopOnWeb runs on the in-memory
/// database, where user ids are regenerated on every boot.
/// </para>
/// </remarks>
internal sealed class MaxioReferenceFactory
{
    /// <summary>
    /// Advanced Billing accepts references up to 255 characters and rejects longer ones with a 422.
    /// </summary>
    internal const int MaxReferenceLength = 255;

    private readonly string _prefix;

    public MaxioReferenceFactory(string prefix)
    {
        _prefix = prefix.Trim();
    }

    /// <summary>Reference identifying the billing customer that stands for an eShopOnWeb shopper.</summary>
    public string ForCustomer(string email) => Compose($"{_prefix}:customer:{Normalize(email)}");

    /// <summary>
    /// Reference for the shopper's <paramref name="sequence"/>-th subscription to
    /// <paramref name="planHandle"/>. The sequence lets a shopper re-subscribe after cancelling while
    /// still making a replay of the same enrolment collide.
    /// </summary>
    public string ForSubscription(string email, string planHandle, int sequence) =>
        Compose($"{_prefix}:subscription:{Normalize(email)}:{planHandle}:{sequence}");

    /// <summary>
    /// Reference scoped to a caller-supplied idempotency key, which takes over from the plan/sequence
    /// derivation when the caller wants explicit control over what counts as the same request.
    /// </summary>
    public string ForSubscription(string email, string idempotencyKey) =>
        Compose($"{_prefix}:subscription:{Normalize(email)}:key:{idempotencyKey.Trim()}");

    /// <summary>True when <paramref name="reference"/> was issued by this integration.</summary>
    public bool IsOwned(string? reference) =>
        reference is not null && reference.StartsWith($"{_prefix}:", StringComparison.Ordinal);

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private string Compose(string candidate)
    {
        if (candidate.Length <= MaxReferenceLength)
        {
            return candidate;
        }

        // Collapse to a digest rather than truncating: truncation could make two distinct shoppers or
        // plans share a reference, which would silently hand one shopper another's subscription.
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidate)));
        return $"{_prefix}:sha256:{digest.ToLowerInvariant()}";
    }
}
