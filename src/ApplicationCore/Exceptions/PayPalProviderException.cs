using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PayPalProviderException : Exception
{
    public PayPalProviderException(string message, int statusCode, string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        DebugId = debugId;
    }

    public int StatusCode { get; }
    public string? DebugId { get; }
}
