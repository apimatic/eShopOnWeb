using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The identity of an eShopOnWeb user as it is presented to the billing system.
/// This is provider-agnostic: it carries only the stable, human-meaningful details
/// a billing system needs to find-or-create a customer record.
/// </summary>
public class SubscriberInfo
{
    public SubscriberInfo(string reference, string email, string firstName, string lastName)
    {
        Reference = Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = Guard.Against.NullOrWhiteSpace(firstName, nameof(firstName));
        LastName = Guard.Against.NullOrWhiteSpace(lastName, nameof(lastName));
    }

    /// <summary>
    /// Stable, unique key for this user in the billing system. The integration uses the
    /// user's e-mail address (which is also the eShopOnWeb user name) so that the mapping
    /// survives an in-memory-database restart, where user Ids are regenerated.
    /// </summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Builds subscriber details from an eShopOnWeb user name / e-mail address. Billing
    /// systems typically require a first and last name, which eShopOnWeb does not store,
    /// so a best-effort display name is derived from the e-mail local part.
    /// </summary>
    public static SubscriberInfo FromEmail(string email)
    {
        Guard.Against.NullOrWhiteSpace(email, nameof(email));

        var normalized = email.Trim().ToLowerInvariant();
        var localPart = normalized.Split('@')[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? normalized : localPart;

        return new SubscriberInfo(
            reference: normalized,
            email: normalized,
            firstName: firstName,
            lastName: "eShopOnWeb");
    }
}
