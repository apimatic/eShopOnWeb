namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>Attributes used to create a billing-provider customer for an eShopOnWeb user.</summary>
public class NewBillingCustomer
{
    public NewBillingCustomer(string reference, string email, string firstName, string lastName)
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
