using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Builds the deterministic identifiers that make the integration idempotent.
/// </summary>
internal static class MaxioCustomerReference
{
    /// <summary>
    /// The Maxio customer <c>reference</c> for an eShopOnWeb account. Maxio enforces uniqueness on
    /// this value, so deriving it from the account name is what guarantees one billing customer per
    /// shopper even if two signup requests race.
    /// <para>
    /// The user name is used rather than the Identity primary key on purpose: it is stable across
    /// database resets (including the in-memory provider), so a restart never orphans the customer
    /// that Maxio already holds.
    /// </para>
    /// </summary>
    public static string ForUser(string prefix, string userName)
    {
        var normalized = userName.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(prefix) ? normalized : $"{prefix.Trim()}:{normalized}";
    }

    /// <summary>
    /// The <c>uniqueness_token</c> for a subscribe attempt. Maxio rejects a second POST carrying the
    /// same token within 60 minutes, which is what makes retrying a create whose response was lost
    /// safe across processes and instances.
    /// <para>
    /// The token identifies one logical attempt, not the (shopper, plan) pair forever. When the
    /// caller supplies an idempotency key that key defines the attempt. Otherwise the attempt is
    /// bucketed by time, so a double-clicked button shares a token while a genuine retry after a
    /// rejected attempt gets a fresh one - a permanently fixed token would let one failed attempt
    /// lock the shopper out of that plan for the full duplicate-prevention hour.
    /// </para>
    /// </summary>
    public static string UniquenessToken(string customerReference, string planHandle, string? idempotencyKey, int windowSeconds, DateTimeOffset now)
    {
        var attempt = string.IsNullOrWhiteSpace(idempotencyKey)
            ? $"window:{now.ToUnixTimeSeconds() / Math.Max(1, windowSeconds)}"
            : $"key:{idempotencyKey!.Trim()}";

        var material = $"{customerReference}|{planHandle}|{attempt}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return new Guid(hash.AsSpan(0, 16)).ToString("D");
    }
}
