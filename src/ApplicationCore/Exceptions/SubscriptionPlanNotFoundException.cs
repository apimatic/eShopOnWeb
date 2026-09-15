using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionPlanNotFoundException : BillingException
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found in the configured product family.", HttpStatusCode.NotFound)
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
