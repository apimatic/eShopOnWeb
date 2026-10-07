using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum OrderPaymentError
{
    NotPaid,
    NothingToRefund,
    InvalidAmount,
    ExceedsRefundable,
    AmountNotRepresentable
}

/// <summary>A payment or refund request that breaks a business rule of the order.</summary>
public class OrderPaymentException : Exception
{
    public OrderPaymentException(OrderPaymentError error, string message) : base(message)
    {
        Error = error;
    }

    public OrderPaymentError Error { get; }
}
