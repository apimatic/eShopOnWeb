using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(string message, int statusCode, string? debugId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        DebugId = debugId;
    }

    public int StatusCode { get; }
    public string? DebugId { get; }
}
