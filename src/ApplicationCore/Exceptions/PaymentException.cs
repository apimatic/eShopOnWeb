using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation could not be completed for a business reason the caller/operator can act on
/// (e.g. card declined, authorization expired and un-renewable, refund exceeds captured amount).
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }
    public PaymentException(string message, Exception inner) : base(message, inner) { }
}
