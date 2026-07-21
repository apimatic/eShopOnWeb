using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The eShopOnWeb-side facts needed to ensure a matching customer record exists with the billing
/// provider. <see cref="Reference"/> is the stable idempotency key (the eShopOnWeb user's identity
/// reference, e.g. <c>User.Identity.Name</c>).
/// </summary>
public class BillingCustomerProfile
{
    public BillingCustomerProfile(string reference, string email, string firstName, string lastName)
    {
        Guard.Against.NullOrEmpty(reference, nameof(reference));
        Guard.Against.NullOrEmpty(email, nameof(email));
        Guard.Against.NullOrEmpty(firstName, nameof(firstName));
        Guard.Against.NullOrEmpty(lastName, nameof(lastName));

        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public string Reference { get; }
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }
}
