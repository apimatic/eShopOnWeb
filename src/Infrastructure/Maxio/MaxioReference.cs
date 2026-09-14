using System;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Builds the reference values eShopOnWeb stores on Maxio records.
/// <para>
/// A customer reference is the join key between an eShopOnWeb user and their Maxio customer, and
/// Maxio enforces that it is unique per site. Deriving it deterministically from the user - rather
/// than storing a generated id locally - is what makes "ensure a customer exists" idempotent even
/// across application restarts and across hosts.
/// </para>
/// </summary>
public static class MaxioReference
{
    private const int MaxLength = 100;

    /// <summary>
    /// Returns the customer reference for an eShopOnWeb user: the configured prefix followed by a
    /// normalised form of the user's stable external id.
    /// </summary>
    public static string ForCustomer(string? prefix, string externalUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalUserId);

        var normalizedPrefix = Normalize(prefix ?? string.Empty);
        var normalizedId = Normalize(externalUserId);

        if (normalizedId.Length == 0)
        {
            throw new ArgumentException(
                "The user identity does not contain any character usable in a billing reference.",
                nameof(externalUserId));
        }

        var reference = normalizedPrefix.Length > 0 ? $"{normalizedPrefix}-{normalizedId}" : normalizedId;
        return reference.Length <= MaxLength ? reference : reference[..MaxLength];
    }

    /// <summary>
    /// Lower-cases the value and keeps only characters that are safe both in a URL query and in a
    /// human-readable identifier, collapsing every run of anything else into a single hyphen.
    /// </summary>
    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSeparator = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '@')
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }

                pendingSeparator = false;
                builder.Append(character);
            }
            else
            {
                pendingSeparator = builder.Length > 0;
            }
        }

        return builder.ToString();
    }
}
