using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PayPalGatewayException : Exception
{
    public PayPalGatewayException(string message) : base(message)
    {
    }

    public PayPalGatewayException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public string? PayPalDebugId { get; init; }
    public string? PayPalName { get; init; }
    public int? HttpStatus { get; init; }
}
