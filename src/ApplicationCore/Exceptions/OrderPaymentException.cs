using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a payment operation is attempted against an order that is not in a state
/// that allows it (e.g. capturing an order that was never authorized).
/// </summary>
public class OrderPaymentException : Exception
{
    public OrderPaymentException(string message) : base(message)
    {
    }
}
