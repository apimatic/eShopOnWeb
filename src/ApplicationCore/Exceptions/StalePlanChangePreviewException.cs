using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The proration basis moved between previewing a plan change and confirming it. The commit is
/// refused so the customer is never charged an amount other than the one they were shown.
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException(decimal previewedPaymentDue, decimal currentPaymentDue)
        : base($"The previewed amount of {previewedPaymentDue:0.00} no longer matches the current amount of {currentPaymentDue:0.00}. Preview the plan change again before confirming.")
    {
        PreviewedPaymentDue = previewedPaymentDue;
        CurrentPaymentDue = currentPaymentDue;
    }

    public decimal PreviewedPaymentDue { get; }

    public decimal CurrentPaymentDue { get; }
}
