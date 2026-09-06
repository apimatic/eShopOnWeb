using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Everything the billing gateway needs to create the billing counterpart of an eShopOnWeb user.
/// </summary>
public class NewCustomerRequest
{
    public NewCustomerRequest(string reference, string email, string firstName, string lastName)
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
}
