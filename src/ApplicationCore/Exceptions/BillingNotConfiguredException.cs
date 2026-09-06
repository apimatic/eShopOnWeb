namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Subscription billing has not been configured for this deployment. The rest of the app - catalog,
/// basket, orders - is unaffected; only the subscription endpoints are unavailable.
/// </summary>
public class BillingNotConfiguredException : BillingException
{
    public BillingNotConfiguredException(string message) : base(message)
    {
    }
}
