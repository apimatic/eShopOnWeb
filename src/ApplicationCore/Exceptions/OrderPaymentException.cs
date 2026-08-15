using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// An attempted payment state transition is not valid (e.g. capturing an order that is not
/// authorized, or refunding beyond the captured amount). Maps to HTTP 409 Conflict.
/// </summary>
public class OrderPaymentException : Exception
{
    public OrderPaymentException(string message) : base(message) { }

    public OrderPaymentException(string message, Exception innerException)
        : base(message, innerException) { }
}
