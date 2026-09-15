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

    public HttpStatusCode StatusCode { get; }
}

public class PaymentNotFoundException : PaymentException
{
    public PaymentNotFoundException(string message) : base(message, HttpStatusCode.NotFound)
    {
    }
}

public class PaymentForbiddenException : PaymentException
{
    public PaymentForbiddenException(string message) : base(message, HttpStatusCode.Forbidden)
    {
    }
}

public class PaymentConflictException : PaymentException
{
    public PaymentConflictException(string message) : base(message, HttpStatusCode.Conflict)
    {
    }
}

public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string message)
        : base(message, HttpStatusCode.UnprocessableEntity)
    {
    }
}
