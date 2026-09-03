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

    public PaymentException(string message, Exception inner, HttpStatusCode statusCode = HttpStatusCode.BadGateway, string? debugId = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string? DebugId { get; }
}

public sealed class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException()
        : base("PayPal required a shopper challenge (3-D Secure / payer-action). This integration does not collect browser approval.", HttpStatusCode.Conflict)
    {
    }
}
