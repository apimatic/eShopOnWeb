namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class CreateBillingSubscription
{
    public CreateBillingSubscription(string productHandle, int customerId, string reference, string paymentCollectionMethod)
    {
        ProductHandle = productHandle;
        CustomerId = customerId;
        Reference = reference;
        PaymentCollectionMethod = paymentCollectionMethod;
    }

    public string ProductHandle { get; }
    public int CustomerId { get; }
    public string Reference { get; }
    public string PaymentCollectionMethod { get; }
}
