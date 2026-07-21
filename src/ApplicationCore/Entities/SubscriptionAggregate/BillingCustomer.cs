namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing provider's customer record, keyed by the eShopOnWeb user's stable reference
/// (email/username) so repeated subscribe calls for the same user are idempotent.
/// </summary>
public class BillingCustomer
{
    public BillingCustomer(int id, string reference, string email)
    {
        Id = id;
        Reference = reference;
        Email = email;
    }

    public int Id { get; }
    public string Reference { get; }
    public string Email { get; }
}
