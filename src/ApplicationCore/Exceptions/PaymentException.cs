using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(string message, int statusCode, string? debugId = null, string? issue = null)
        : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        Issue = issue;
    }

    public PaymentException(string message, int statusCode, Exception innerException, string? debugId = null, string? issue = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        Issue = issue;
    }

    public int StatusCode { get; }
    public string? DebugId { get; }
    public string? Issue { get; }
}
