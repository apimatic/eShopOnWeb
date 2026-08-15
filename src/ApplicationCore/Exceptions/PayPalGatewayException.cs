using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal rejected or could not complete a request (a declined card, a validation issue, or an
/// upstream error). Carries a caller-safe message describing what PayPal reported. Maps to HTTP 502.
/// </summary>
public class PayPalGatewayException : Exception
{
    public PayPalGatewayException(string message) : base(message) { }

    public PayPalGatewayException(string message, Exception innerException)
        : base(message, innerException) { }
}
