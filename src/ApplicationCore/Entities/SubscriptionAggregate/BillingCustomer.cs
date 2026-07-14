namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class BillingCustomer
{
    public BillingCustomer(int providerCustomerId, string reference, string email)
    {
        ProviderCustomerId = providerCustomerId;
        Reference = reference;
        Email = email;
    }

    public int ProviderCustomerId { get; }
    public string Reference { get; }
    public string Email { get; }
}
