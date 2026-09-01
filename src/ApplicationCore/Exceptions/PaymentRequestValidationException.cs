using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The payment request is invalid (missing card details, unknown payment source, bad amount, ...).
/// </summary>
public class PaymentRequestValidationException : Exception
{
    public PaymentRequestValidationException(string message) : base(message)
    {
    }
}
