using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Derives the Maxio-side customer identity from an eShopOnWeb user.
/// </summary>
/// <remarks>
/// Maxio permits only one customer per <c>reference</c>, so the reference is the integration's natural
/// idempotency key: as long as it is derived deterministically from the caller's own identity, a repeated
/// subscribe can never produce a second customer, even across processes.
/// </remarks>
internal static class MaxioSubscriberMapper
{
    private const string ReferencePrefix = "eshoponweb-";

    /// <summary>Longest reference we will send verbatim before falling back to a digest.</summary>
    private const int MaxReadableReferenceLength = 100;

    /// <summary>
    /// Builds the stable customer reference for an email address. Case and surrounding whitespace are
    /// normalised so that the same user always maps to the same reference.
    /// </summary>
    public static string ToCustomerReference(string email)
    {
        var normalized = Normalize(email);

        var readable = ReferencePrefix + normalized;
        if (readable.Length <= MaxReadableReferenceLength)
        {
            return readable;
        }

        // Unusually long address: keep the reference bounded but still deterministic.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return ReferencePrefix + Convert.ToHexString(digest, 0, 16).ToLowerInvariant();
    }

    /// <summary>
    /// Splits an email local part into the first/last name Maxio requires. eShopOnWeb's identity record
    /// carries no real name, so this is the best available source; the customer's email is stored verbatim
    /// alongside it either way.
    /// </summary>
    public static (string FirstName, string LastName) ToCustomerName(string email)
    {
        var normalized = Normalize(email);
        var localPart = normalized.Split('@')[0];

        var words = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Capitalize)
            .ToArray();

        return words.Length switch
        {
            0 => ("eShopOnWeb", "Customer"),
            1 => (words[0], "Customer"),
            _ => (words[0], string.Join(' ', words.Skip(1)))
        };
    }

    private static string Normalize(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string Capitalize(string word) =>
        word.Length == 0 ? word : char.ToUpper(word[0], CultureInfo.InvariantCulture) + word.Substring(1);
}
