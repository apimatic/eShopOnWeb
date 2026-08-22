using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string message, string? debugId = null, string? issue = null)
        : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        Issue = issue;
    }

    public int StatusCode { get; }
    public string? DebugId { get; }
    public string? Issue { get; }
}
