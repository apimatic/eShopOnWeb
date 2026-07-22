using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing provider's record of an eShopOnWeb user. <see cref="Reference"/> is the stable
/// eShopOnWeb identity (the signed-in user's email/username) and is what makes customer
/// creation idempotent across repeated subscribe attempts.
/// </summary>
public class BillingCustomer
{
    public BillingCustomer(int id, string reference, string email, string firstName, string lastName)
    {
        Guard.Against.NullOrEmpty(reference, nameof(reference));

        Id = id;
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public int Id { get; private set; }

    public string Reference { get; private set; }

    public string Email { get; private set; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }
}
