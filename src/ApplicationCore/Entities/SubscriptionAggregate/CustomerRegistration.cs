using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The details used to create — idempotently, on <see cref="Reference"/> — the provider-side
/// customer record for an eShopOnWeb user.
/// </summary>
public class CustomerRegistration
{
    public CustomerRegistration(string reference, string email, string firstName, string lastName)
    {
        Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        Guard.Against.NullOrWhiteSpace(email, nameof(email));
        Guard.Against.NullOrWhiteSpace(firstName, nameof(firstName));
        Guard.Against.NullOrWhiteSpace(lastName, nameof(lastName));

        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Builds a registration from an eShopOnWeb user reference. eShopOnWeb identity only carries a
    /// username (which is the email), so the name fields are derived from the local part rather than
    /// being left blank — the provider requires both.
    /// </summary>
    public static CustomerRegistration FromUserReference(string userReference)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var localPart = userReference.Split('@')[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? userReference : localPart;

        return new CustomerRegistration(userReference, userReference, firstName, "eShopOnWeb");
    }
}
