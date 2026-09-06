namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The plan requires a stored payment method before Maxio will create the subscription.
/// Capturing card details (Chargify.js plus the 3-DS post-authentication flow) is outside
/// the scope of this integration, so we refuse the enrollment rather than half-perform it.
/// </summary>
public class PaymentMethodRequiredException : BillingException
{
    public PaymentMethodRequiredException(string planHandle)
        : base($"Plan '{planHandle}' requires a stored payment method, which this API does not capture.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
