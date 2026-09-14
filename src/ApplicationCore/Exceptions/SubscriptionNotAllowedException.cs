namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the billing provider rejects an otherwise well-formed request, for example because
/// the plan requires a stored payment method that the shopper does not have.
/// </summary>
public class SubscriptionNotAllowedException : SubscriptionBillingException
{
    public SubscriptionNotAllowedException(string message) : base(message)
    {
    }
}
