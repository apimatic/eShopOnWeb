using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(string message, HttpStatusCode statusCode = HttpStatusCode.BadRequest) : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public class ResourceNotFoundException : PaymentException
{
    public ResourceNotFoundException(string message) : base(message, HttpStatusCode.NotFound)
    {
    }
}

public class PaymentConflictException : PaymentException
{
    public PaymentConflictException(string message) : base(message, HttpStatusCode.Conflict)
    {
    }
}

public class AuthorizationCannotBeRenewedException : PaymentException
{
    public AuthorizationCannotBeRenewedException(string message)
        : base(message, HttpStatusCode.Conflict)
    {
    }
}

public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException()
        : base("PayPal required a shopper challenge in the browser (3-D Secure / payer action). This integration does not perform a browser approval round-trip.", HttpStatusCode.Conflict)
    {
    }
}

public class PayPalGatewayException : PaymentException
{
    public PayPalGatewayException(string message, HttpStatusCode statusCode = HttpStatusCode.BadGateway, string? debugId = null, string? issue = null)
        : base(message, statusCode)
    {
        DebugId = debugId;
        Issue = issue;
    }

    public string? DebugId { get; }
    public string? Issue { get; }
}
