namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The plan cannot be subscribed to because the billing system demands a stored payment profile,
/// and eShopOnWeb does not capture card details for subscriptions.
/// </summary>
public class PaymentMethodRequiredException : BillingException
{
    public PaymentMethodRequiredException(string planHandle)
        : base($"Plan '{planHandle}' requires a payment method to be captured before subscribing, which this application does not support.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
