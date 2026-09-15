namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionPlanNotFoundException : BillingException
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found.", 404)
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
