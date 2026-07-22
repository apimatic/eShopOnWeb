namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The details used to create a provider-side customer record for an eShopOnWeb user.
/// </summary>
public class NewBillingCustomer
{
    public NewBillingCustomer(string reference, string email, string firstName, string lastName)
    {
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>The stable eShopOnWeb identity; the provider enforces uniqueness on it.</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }
}
