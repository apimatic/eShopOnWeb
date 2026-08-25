using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class RefundExceedsCapturedAmountException : Exception
{
    public RefundExceedsCapturedAmountException(decimal requestedAmount, decimal remainingRefundableAmount)
        : base($"Refund amount {requestedAmount} exceeds the remaining refundable amount {remainingRefundableAmount}.")
    {
    }
}
