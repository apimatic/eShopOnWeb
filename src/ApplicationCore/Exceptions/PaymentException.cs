using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment business rule was violated (e.g. authorizing an order that is not awaiting payment,
/// fulfilling one that was never authorized, or refunding more than was captured).
/// Maps to HTTP 409 Conflict.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }
}
