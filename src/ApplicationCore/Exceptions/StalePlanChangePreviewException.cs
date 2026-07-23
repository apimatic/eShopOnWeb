using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the cost of a plan change moved between the preview the customer confirmed and
/// the commit. The change is refused rather than charging an amount the customer never saw
/// (UC3).
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException(decimal expectedPaymentDue, decimal actualPaymentDue)
        : base($"The previewed cost has changed (confirmed {expectedPaymentDue:0.00}, now {actualPaymentDue:0.00}). " +
               "Request a fresh preview before confirming.")
    {
        ExpectedPaymentDue = expectedPaymentDue;
        ActualPaymentDue = actualPaymentDue;
    }

    public decimal ExpectedPaymentDue { get; }

    public decimal ActualPaymentDue { get; }
}
