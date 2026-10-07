using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>An order's payment state kept changing under a request; the caller may retry.</summary>
public class PaymentConcurrencyException : Exception
{
    public PaymentConcurrencyException(int orderId, Exception innerException)
        : base($"Order {orderId} is being changed by another request. Please retry.", innerException)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}
