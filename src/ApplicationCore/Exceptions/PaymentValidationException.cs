using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The payment request itself is invalid (e.g. missing card details, unknown saved card,
/// shopper action required by the payment provider). Maps to HTTP 422.
/// </summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message)
    {
    }
}
