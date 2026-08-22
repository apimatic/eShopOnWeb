using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(string message, HttpStatusCode statusCode = HttpStatusCode.BadRequest, string? debugId = null)
        : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string? DebugId { get; }
}
