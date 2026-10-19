namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingOutcomeUnknownException : SubscriptionBillingException
{
    public BillingOutcomeUnknownException()
        : base(
            "The billing provider did not confirm whether this request completed. It will not be sent again until the outcome is known.",
            503)
    {
    }
}
