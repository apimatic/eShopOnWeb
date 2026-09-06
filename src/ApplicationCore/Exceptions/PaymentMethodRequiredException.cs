using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a plan cannot be subscribed to without a stored payment instrument. eShopOnWeb does not
/// capture card or bank details, so such plans are not subscribable through this API.
/// </summary>
public class PaymentMethodRequiredException : Exception
{
    public PaymentMethodRequiredException(string planHandle)
        : base($"Plan '{planHandle}' requires a payment method on file, which this application does not collect.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
