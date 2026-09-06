using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a plan requires a stored payment method before signup. This integration deliberately
/// does not collect card details (no PCI surface, no 3-D Secure flow), so such plans cannot be
/// subscribed to from here.
/// </summary>
public class PaymentMethodRequiredException : Exception
{
    public PaymentMethodRequiredException(string planHandle)
        : base($"Plan '{planHandle}' requires a stored payment method, which this integration does not collect.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
