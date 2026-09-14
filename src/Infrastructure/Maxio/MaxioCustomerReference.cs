using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Derives the provider-side customer reference for an eShopOnWeb user.
/// </summary>
/// <remarks>
/// The reference is what makes "ensure a customer exists" idempotent: the provider enforces that at most
/// one customer carries a given reference, so a repeated signup finds the first one instead of creating a
/// second. It therefore has to be derived from something stable — the user name, not the ASP.NET Identity
/// key, which is regenerated whenever the app runs on the in-memory database.
/// </remarks>
public static class MaxioCustomerReference
{
    /// <summary>Conservative cap; longer identities are folded into a digest rather than truncated.</summary>
    private const int MaxLength = 100;

    public const string DefaultPrefix = "eshoponweb";

    public static string For(string userName, string? prefix = null)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A customer reference requires a user name.", nameof(userName));
        }

        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? DefaultPrefix : prefix.Trim();
        var normalizedUser = userName.Trim().ToLowerInvariant();

        var reference = $"{normalizedPrefix}-{normalizedUser}";
        if (reference.Length <= MaxLength)
        {
            return reference;
        }

        // Still deterministic, so the same user keeps resolving to the same provider customer.
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUser)))
            .ToLowerInvariant();

        return $"{normalizedPrefix}-{digest}";
    }
}
