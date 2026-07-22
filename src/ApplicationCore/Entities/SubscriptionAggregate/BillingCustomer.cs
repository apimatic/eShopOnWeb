namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing provider's record of an eShopOnWeb user. <see cref="Reference"/> carries the stable
/// eShopOnWeb identity (the user's email / username) and is what makes repeated subscribe calls idempotent.
/// </summary>
public class BillingCustomer
{
    public BillingCustomer(int id, string? reference, string? email, string? firstName, string? lastName)
    {
        Id = id;
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public int Id { get; }

    public string? Reference { get; }

    public string? Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}
