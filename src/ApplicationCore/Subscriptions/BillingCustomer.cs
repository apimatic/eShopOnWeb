namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The billing-provider customer record an eShopOnWeb user is mapped onto.
/// <see cref="Reference"/> is the eShopOnWeb-side identifier and is what makes
/// "ensure a customer exists" idempotent.
/// </summary>
public class BillingCustomer
{
    public BillingCustomer(long id, string? reference, string? firstName, string? lastName, string? email)
    {
        Id = id;
        Reference = reference;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
    }

    public long Id { get; }

    public string? Reference { get; }

    public string? FirstName { get; }

    public string? LastName { get; }

    public string? Email { get; }
}
