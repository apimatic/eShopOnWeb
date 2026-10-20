using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Derives the deterministic Maxio customer reference for an eShopOnWeb user: "eshop-" followed by
/// the first 32 lowercase hex characters of SHA-256(upper-invariant user name). Stable across restarts,
/// carries no raw email, and stays within 40 characters.
/// </summary>
public static class CustomerReference
{
    private const string Prefix = "eshop-";
    private const int HashChars = 32;

    public static string ForUser(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) throw new ArgumentException("A user name is required.", nameof(userName));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToUpperInvariant()));
        return Prefix + Convert.ToHexString(hash).ToLowerInvariant()[..HashChars];
    }
}
