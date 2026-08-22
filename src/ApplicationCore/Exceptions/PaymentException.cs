using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(string message, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public PaymentException(string message, Exception inner, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string? detail = null)
        : base(string.IsNullOrWhiteSpace(detail)
            ? "PayPal required a shopper approval step in the browser (for example 3-D Secure). This integration does not collect money through a browser round-trip."
            : detail, HttpStatusCode.Conflict)
    {
    }
}
