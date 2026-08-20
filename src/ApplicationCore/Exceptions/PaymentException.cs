using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public PaymentException(string message, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public PaymentException(string message, Exception inner, HttpStatusCode statusCode = HttpStatusCode.BadGateway)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string message)
        : base(message, HttpStatusCode.Conflict)
    {
    }
}
