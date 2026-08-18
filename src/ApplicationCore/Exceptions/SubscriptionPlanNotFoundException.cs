namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionPlanNotFoundException : BillingException
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base(404, $"No subscription plan was found with handle '{productHandle}'.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
