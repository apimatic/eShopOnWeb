using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal rejected or could not complete an operation (e.g. a declined card, or an authorization
/// that can no longer be reauthorized). The message is phrased so an operator can act on it and,
/// where PayPal supplied one, carries PayPal's own issue/description and debug id.
/// Maps to HTTP 502 Bad Gateway.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message) : base(message) { }

    public PaymentGatewayException(string message, Exception inner) : base(message, inner) { }
}
