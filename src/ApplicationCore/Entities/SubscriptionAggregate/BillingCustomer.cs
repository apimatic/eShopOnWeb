namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing provider's record of an eShopOnWeb user. <see cref="Reference"/> is the
/// stable eShopOnWeb identity (email / username) and is what makes customer creation idempotent.
/// </summary>
public class BillingCustomer
{
    public BillingCustomer(int providerCustomerId, string reference, string email,
        string? firstName, string? lastName)
    {
        ProviderCustomerId = providerCustomerId;
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public int ProviderCustomerId { get; }

    public string Reference { get; }

    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}
