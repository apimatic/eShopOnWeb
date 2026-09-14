using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Builds the deterministic references and uniqueness tokens that make enrolment idempotent.
/// </summary>
/// <remarks>
/// Every value is a pure function of the shopper and the plan, so the same logical subscribe request
/// produces the same reference on every attempt, from any instance of the application, without any
/// local state. That is what lets a restarted (or in-memory backed) eShopOnWeb reconnect a shopper to
/// the customer record Maxio already holds for them.
/// </remarks>
public class MaxioReferenceFactory
{
    /// <summary>
    /// Longest reference this factory emits before falling back to a hash. Comfortably inside what
    /// Maxio accepts while staying readable in the Maxio UI.
    /// </summary>
    private const int MaxReferenceLength = 100;

    private readonly string _prefix;

    public MaxioReferenceFactory(string prefix)
    {
        _prefix = string.IsNullOrWhiteSpace(prefix) ? "eshoponweb" : prefix.Trim();
    }

    /// <summary>
    /// Reference for the Maxio customer that represents an eShopOnWeb shopper.
    /// </summary>
    public string CustomerReference(string userKey)
    {
        if (string.IsNullOrWhiteSpace(userKey))
        {
            throw new ArgumentException("A subscriber key is required to build a customer reference.", nameof(userKey));
        }

        var candidate = $"{_prefix}-{userKey.Trim().ToLowerInvariant()}";
        return candidate.Length <= MaxReferenceLength
            ? candidate
            : $"{_prefix}-{Hash(userKey.Trim().ToLowerInvariant())[..40]}";
    }

    /// <summary>
    /// Reference for a subscription. <paramref name="generation"/> is how many subscriptions to this
    /// plan the shopper already has, so a shopper who cancels and signs up again gets a distinct,
    /// still deterministic, reference rather than colliding with the old one.
    /// </summary>
    public string SubscriptionReference(string customerReference, string planHandle, int generation)
    {
        var suffix = generation <= 0
            ? planHandle
            : $"{planHandle}-{(generation + 1).ToString(CultureInfo.InvariantCulture)}";

        var candidate = $"{customerReference}-{suffix}";
        return candidate.Length <= MaxReferenceLength
            ? candidate
            : $"{_prefix}-{Hash(candidate)[..40]}";
    }

    /// <summary>
    /// Token that lets Maxio collapse duplicate deliveries of the same logical write for 60 minutes.
    /// </summary>
    /// <param name="scope">Operation being guarded, e.g. <c>customer</c> or <c>subscription</c>.</param>
    /// <param name="parts">
    /// Everything that identifies this particular attempt. Two attempts that should be treated as the
    /// same write must produce the same parts; a legitimately new write must not.
    /// </param>
    public string UniquenessToken(string scope, params string[] parts)
    {
        var material = string.Join('|', parts);
        return $"{_prefix}-{scope}-{Hash(material)}";
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
