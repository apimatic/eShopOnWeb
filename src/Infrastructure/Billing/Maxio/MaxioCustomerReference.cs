using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Derives the Maxio customer <c>reference</c> for an eShopOnWeb user.
/// <para>
/// The reference is this integration's idempotency key: Maxio permits at most one customer per reference
/// value, and lookup-by-reference is the only exact-match customer search the API offers. It is derived
/// purely from the login name - no database row is involved - so the same user maps to the same Maxio
/// customer across restarts even though eShopOnWeb runs on the in-memory provider here.
/// </para>
/// <para>
/// A digest rather than the raw login name: the reference becomes an identifier inside a third-party
/// system, and hashing keeps the e-mail address out of it while staying stable, unique and URL-safe. The
/// address itself is still sent on the customer record, where it belongs.
/// </para>
/// </summary>
public static class MaxioCustomerReference
{
    private const string Prefix = "eshoponweb-";
    private const int HexLength = 32;

    public static string For(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("A user name is required to derive a billing reference.", nameof(userName));

        var normalized = userName.Trim().ToLowerInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        return Prefix + Convert.ToHexString(digest).ToLowerInvariant().Substring(0, HexLength);
    }
}
