using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class RefundExceedsCapturedAmountException : Exception
{
    public RefundExceedsCapturedAmountException(decimal requested, decimal remainingRefundable)
        : base($"Refund of {requested:0.00} exceeds the remaining refundable amount of {remainingRefundable:0.00} for this order.")
    {
    }
}
