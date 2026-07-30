namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The eShopOnWeb identity being enrolled, projected onto the fields Maxio needs to
/// create/locate a customer. <see cref="Reference"/> is the idempotency key: it maps a
/// single eShopOnWeb user to exactly one Maxio customer, so it must be stable for that
/// user across application restarts (the app uses the login/username for this).
/// </summary>
public class BillingCustomer
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
