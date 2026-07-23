namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The details needed to create a provider-side customer for an eShopOnWeb user.
/// <see cref="Reference"/> is the stable eShopOnWeb user name and is what makes the operation
/// idempotent across repeated subscribe attempts (plan.md §4.4).
/// </summary>
public class BillingCustomerRegistration
{
    public BillingCustomerRegistration(string reference, string email, string firstName, string lastName)
    {
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
