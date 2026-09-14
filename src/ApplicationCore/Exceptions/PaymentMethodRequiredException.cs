using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a plan cannot be subscribed to without a stored payment method. eShopOnWeb does not
/// capture card details, so such a plan is not signable from this application.
/// </summary>
public class PaymentMethodRequiredException : Exception
{
    public PaymentMethodRequiredException(string planHandle)
        : base($"Plan '{planHandle}' requires a stored payment method, which eShopOnWeb does not collect.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
