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

public class PaymentNotFoundException : PaymentException
{
    public PaymentNotFoundException(string message)
        : base(message, HttpStatusCode.NotFound)
    {
    }
}

public class PaymentForbiddenException : PaymentException
{
    public PaymentForbiddenException(string message)
        : base(message, HttpStatusCode.Forbidden)
    {
    }
}

public class PaymentConflictException : PaymentException
{
    public PaymentConflictException(string message)
        : base(message, HttpStatusCode.Conflict)
    {
    }
}

public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string message, string? debugId = null)
        : base(message, HttpStatusCode.UnprocessableEntity, debugId)
    {
    }
}

public class AuthorizationCannotBeRenewedException : PaymentException
{
    public AuthorizationCannotBeRenewedException(string message, string? debugId = null)
        : base(message, HttpStatusCode.Conflict, debugId)
    {
    }
}
