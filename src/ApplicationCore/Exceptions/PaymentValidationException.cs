using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The caller's payment request is invalid given the current state (e.g. paying an order twice, refunding
/// more than was captured, cancelling after fulfilment). Maps to a 400/409-class response.
/// </summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}
