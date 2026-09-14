using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Maps an eShopOnWeb user onto the Maxio customer <c>reference</c>, the field Maxio guarantees is
/// unique per customer. That mapping is what makes the integration idempotent without a local table:
/// Maxio is the system of record, and the reference is derived deterministically from the user name
/// carried in the JWT rather than from a database row (which, on the in-memory provider, would not
/// survive a restart).
/// </summary>
public static class MaxioCustomerReference
{
    private const int MaxLength = 100;

    /// <summary>
    /// Builds the reference for a user: <c>{prefix}-{normalised user name}</c>, lower-cased and
    /// reduced to characters that travel safely in a query string. Over-long names are truncated and
    /// suffixed with a hash of the original so distinct users can never collide.
    /// </summary>
    public static string For(string userName, string prefix)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user name is required to derive a Maxio customer reference.", nameof(userName));
        }

        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? "eshoponweb" : Sanitize(prefix);
        var normalizedUser = Sanitize(userName);
        var reference = $"{normalizedPrefix}-{normalizedUser}";

        if (reference.Length <= MaxLength)
        {
            return reference;
        }

        var digest = ShortHash(userName);
        var room = MaxLength - normalizedPrefix.Length - digest.Length - 2;
        return $"{normalizedPrefix}-{normalizedUser[..Math.Max(0, room)]}-{digest}";
    }

    private static string Sanitize(string value)
    {
        var lowered = value.Trim().ToLowerInvariant();
        var builder = new StringBuilder(lowered.Length);

        foreach (var character in lowered)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '@' or '-'
                ? character
                : '-');
        }

        return builder.ToString();
    }

    private static string ShortHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var builder = new StringBuilder(16);
        for (var i = 0; i < 8; i++)
        {
            builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
