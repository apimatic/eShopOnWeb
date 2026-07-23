using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing provider's record of an eShopOnWeb user, keyed by <see cref="Reference"/>.
/// </summary>
public class BillingCustomer
{
    public BillingCustomer(int id, string reference, string email, string firstName, string lastName)
    {
        Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        Guard.Against.NullOrWhiteSpace(email, nameof(email));

        Id = id;
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>The provider-assigned customer id.</summary>
    public int Id { get; }

    /// <summary>The stable eShopOnWeb reference (the user's email/username).</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }
}
