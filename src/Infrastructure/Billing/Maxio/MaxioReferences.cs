using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Derives the reference strings that tie Maxio records back to eShopOnWeb users.
/// </summary>
/// <remarks>
/// <para>
/// References are the backbone of this integration. Maxio enforces that a customer reference and a
/// subscription reference are each unique within a site, so a reference derived deterministically from
/// the eShopOnWeb user makes "find or create" safe without eShopOnWeb persisting any mapping of its
/// own -- which matters here, because the reference implementation may run against the in-memory
/// database, whose data does not survive a restart.
/// </para>
/// <para>
/// The user name is used rather than the Identity primary key deliberately: eShopOnWeb user names are
/// e-mail addresses and are stable across re-seeds, whereas the generated key is not.
/// </para>
/// </remarks>
public static class MaxioReferences
{
    /// <summary>Separates the segments of a reference. Chosen because it never occurs in an e-mail address or a Maxio handle.</summary>
    private const char Separator = ':';

    /// <summary>
    /// The reference identifying the Maxio customer for an eShopOnWeb user, e.g.
    /// <c>eshoponweb:demouser@microsoft.com</c>.
    /// </summary>
    public static string CustomerReference(string referencePrefix, string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user name is required to derive a Maxio customer reference.", nameof(userName));
        }

        return $"{referencePrefix}{Separator}{userName.Trim().ToLowerInvariant()}";
    }

    /// <summary>
    /// The reference a user's first subscription to a plan gets, e.g.
    /// <c>eshoponweb:demouser@microsoft.com:eshop-pro</c>.
    /// </summary>
    public static string SubscriptionReferenceRoot(string customerReference, string planHandle) =>
        $"{customerReference}{Separator}{planHandle.Trim().ToLowerInvariant()}";

    /// <summary>
    /// Picks the reference for a new subscription: the root when it is free, otherwise the root with the
    /// lowest unused numeric suffix.
    /// </summary>
    /// <remarks>
    /// A suffix is only ever needed when the user is re-subscribing to a plan they previously held and
    /// cancelled, because a live subscription short-circuits the signup before it gets here. Deriving the
    /// suffix from what Maxio already holds keeps the choice deterministic rather than random, so a
    /// retried request lands on the same reference and Maxio rejects the duplicate instead of enrolling twice.
    /// </remarks>
    public static string NextAvailableSubscriptionReference(string referenceRoot, IEnumerable<string?> existingReferences)
    {
        var taken = new HashSet<string>(
            existingReferences.Where(r => !string.IsNullOrEmpty(r)).Select(r => r!),
            StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(referenceRoot))
        {
            return referenceRoot;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = referenceRoot + Separator + suffix.ToString(CultureInfo.InvariantCulture);
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Splits an eShopOnWeb user name into the first and last name Maxio requires on a customer.
    /// </summary>
    /// <remarks>
    /// eShopOnWeb stores no real name for a user, only the e-mail address they sign in with, so the local
    /// part is the best available source. <c>jane.doe@example.com</c> becomes "Jane"/"Doe";
    /// <c>demouser@microsoft.com</c>, which has nothing to split on, becomes "Demouser"/"eShopOnWeb" so the
    /// record is still recognisable in the Maxio UI.
    /// </remarks>
    public static (string FirstName, string LastName) DeriveCustomerName(string userName)
    {
        const string fallbackLastName = "eShopOnWeb";

        var localPart = userName.Split('@')[0];
        var words = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Capitalise)
            .Where(w => w.Length > 0)
            .ToList();

        return words.Count switch
        {
            0 => (userName, fallbackLastName),
            1 => (words[0], fallbackLastName),
            _ => (words[0], string.Join(" ", words.Skip(1)))
        };
    }

    private static string Capitalise(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];
}
