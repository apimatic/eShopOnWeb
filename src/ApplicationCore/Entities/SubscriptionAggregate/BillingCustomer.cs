using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing provider's record of an eShopOnWeb user. <see cref="Reference"/> carries the
/// eShopOnWeb username/email so the same user always maps to the same provider customer.
/// </summary>
public class BillingCustomer
{
    public BillingCustomer(int id, string? reference, string email, string firstName, string lastName)
    {
        Guard.Against.NullOrEmpty(email, nameof(email));

        Id = id;
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public int Id { get; }
    public string? Reference { get; }
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }
}
