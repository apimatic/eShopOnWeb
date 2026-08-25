using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown for a payment-provider failure that does not fit a more specific exception below it
/// (connection failure, an unreadable response, or an unrecognised error) — the catch-all at the
/// payment-gateway boundary so callers only ever have to reason about our own exception types.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message) : base(message)
    {
    }

    public PaymentGatewayException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
