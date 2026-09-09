namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The identity of the eShopOnWeb user being enrolled, projected onto the fields Maxio needs
/// to create a customer. <see cref="Reference"/> is the stable, unique key that guarantees a
/// single Maxio customer per eShopOnWeb user (used for idempotent enrollment).
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
