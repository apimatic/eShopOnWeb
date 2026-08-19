namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Shopper identity used to find or create a Maxio customer. <see cref="Reference"/> is the
/// stable key stored as the Maxio customer <c>reference</c>.
/// </summary>
public sealed class BillingCustomer
{
    public BillingCustomer(string reference, string email, string firstName, string lastName)
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
