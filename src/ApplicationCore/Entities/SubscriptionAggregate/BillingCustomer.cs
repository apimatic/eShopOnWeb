namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A provider-agnostic representation of the billing-side customer record that an eShopOnWeb
/// user maps to. <see cref="Reference"/> is the stable eShopOnWeb identity (email/username, §8),
/// which makes customer creation idempotent.
/// </summary>
public class BillingCustomer
{
    public BillingCustomer(int id, string? reference, string? email)
    {
        Id = id;
        Reference = reference;
        Email = email;
    }

    public int Id { get; }

    public string? Reference { get; }

    public string? Email { get; }
}
