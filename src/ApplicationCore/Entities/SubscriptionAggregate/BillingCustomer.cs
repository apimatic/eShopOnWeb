namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The provider-side customer record linked to an eShopOnWeb user through <see cref="Reference"/>.
/// The reference is the eShopOnWeb username/email (§4.4) and makes customer creation idempotent.
/// </summary>
public class BillingCustomer
{
    public BillingCustomer(int id, string reference, string email, string firstName, string lastName)
    {
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
