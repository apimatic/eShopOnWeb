using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a payment request is malformed or references something the caller may not use (for
/// example, no payment instrument supplied, or a saved card that is not the caller's). Surfaced to
/// the API as a 400 Bad Request.
/// </summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message)
    {
    }
}
