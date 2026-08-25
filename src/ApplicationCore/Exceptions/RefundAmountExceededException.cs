using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when a refund request asks for more than remains refundable on the capture.</summary>
public class RefundAmountExceededException : Exception
{
    public RefundAmountExceededException(decimal requestedAmount, decimal remainingRefundableAmount)
        : base($"Cannot refund {requestedAmount}: only {remainingRefundableAmount} remains refundable on this order's capture.")
    {
    }
}
