using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies an eShopOnWeb user to the billing system in a provider-agnostic way.
/// The <see cref="Reference"/> is the stable, unique key we use to correlate an
/// eShopOnWeb identity with a billing-system customer record so that enrollment is
/// idempotent across requests (and across process restarts).
/// </summary>
public class BillingUser
{
    public BillingUser(string reference, string email, string firstName, string lastName)
    {
        Reference = Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = Guard.Against.NullOrWhiteSpace(firstName, nameof(firstName));
        LastName = Guard.Against.NullOrWhiteSpace(lastName, nameof(lastName));
    }

    /// <summary>Stable, unique identifier for this user within eShopOnWeb (used as the billing customer reference).</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Builds a <see cref="BillingUser"/> from an eShopOnWeb user name / email. eShopOnWeb identities are
    /// keyed by a unique email address, so the email is a stable, unique reference for the billing customer.
    /// </summary>
    public static BillingUser FromUserName(string userName)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var email = userName.Trim();
        var localPart = email.Contains('@', StringComparison.Ordinal)
            ? email[..email.IndexOf('@', StringComparison.Ordinal)]
            : email;
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;

        return new BillingUser(reference: email, email: email, firstName: firstName, lastName: "eShopOnWeb");
    }
}
