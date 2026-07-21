using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A customer record as it exists with the billing provider.
/// </summary>
public class BillingCustomer
{
    public BillingCustomer(int id, string reference, string email)
    {
        Guard.Against.NegativeOrZero(id, nameof(id));
        Guard.Against.NullOrEmpty(reference, nameof(reference));
        Guard.Against.NullOrEmpty(email, nameof(email));

        Id = id;
        Reference = reference;
        Email = email;
    }

    public int Id { get; }
    public string Reference { get; }
    public string Email { get; }
}
