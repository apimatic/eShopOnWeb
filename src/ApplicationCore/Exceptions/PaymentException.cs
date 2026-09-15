using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment-flow error stated in terms a shopper or operator can act on (e.g. "authorization can
/// no longer be renewed; ask the shopper to pay again"). Surfaced to the caller as a 4xx response.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message)
    {
    }

    public PaymentException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
