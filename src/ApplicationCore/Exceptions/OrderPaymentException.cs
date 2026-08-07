using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order's payment state transition is not allowed
/// (e.g. refunding an order that has not been paid).
/// </summary>
public class OrderPaymentException : Exception
{
    public OrderPaymentException(string message) : base(message)
    {
    }
}
