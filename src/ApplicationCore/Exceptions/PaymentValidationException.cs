using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation was requested that is not valid for the current state (for example paying
/// an order twice, cancelling a fulfilled order, or refunding beyond what was captured). Surfaced
/// to the caller as a 400.
/// </summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}
