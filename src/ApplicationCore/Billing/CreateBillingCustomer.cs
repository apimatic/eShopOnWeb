namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class CreateBillingCustomer
{
    public CreateBillingCustomer(string firstName, string lastName, string email, string reference)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        Reference = reference;
    }

    public string FirstName { get; }
    public string LastName { get; }
    public string Email { get; }
    public string Reference { get; }
}
