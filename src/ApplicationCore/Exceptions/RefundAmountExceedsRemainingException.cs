using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when a refund request would refund more than remains available on the captured payment.</summary>
public class RefundAmountExceedsRemainingException : Exception
{
    public decimal RequestedAmount { get; }
    public decimal RemainingRefundableAmount { get; }

    public RefundAmountExceedsRemainingException(decimal requestedAmount, decimal remainingRefundableAmount)
        : base($"Refund of {requestedAmount:0.00} exceeds the {remainingRefundableAmount:0.00} still refundable on this order's captured payment.")
    {
        RequestedAmount = requestedAmount;
        RemainingRefundableAmount = remainingRefundableAmount;
    }
}
