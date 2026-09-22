namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The caller asked to subscribe to a plan handle the configured product family does not offer.
/// A caller error (400), enforced before any create reaches the billing provider.
/// </summary>
public class UnknownPlanException : BillingException
{
    public UnknownPlanException(string planHandle)
        : base($"Plan '{planHandle}' is not an available subscription plan.", BillingErrorKind.InvalidRequest)
    {
    }
}
