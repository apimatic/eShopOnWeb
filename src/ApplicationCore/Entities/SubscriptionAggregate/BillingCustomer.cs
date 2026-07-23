namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The provider-side customer record that an eShopOnWeb user maps onto. The mapping is stateless:
/// <see cref="Reference"/> carries the eShopOnWeb user name (email), which makes customer creation
/// idempotent without eShopOnWeb persisting anything (plan.md §4.4, §8).
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

    public int Id { get; }

    /// <summary>The stable eShopOnWeb user reference (email / username).</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }
}
