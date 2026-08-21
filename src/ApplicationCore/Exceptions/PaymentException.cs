using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public int StatusCode { get; }

    public PaymentException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }

    public PaymentException(string message, Exception innerException, int statusCode = 400)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

public class EntityNotFoundException : PaymentException
{
    public EntityNotFoundException(string message) : base(message, 404)
    {
    }
}

public class PaymentConflictException : PaymentException
{
    public PaymentConflictException(string message) : base(message, 409)
    {
    }
}

public class ForbiddenOperationException : PaymentException
{
    public ForbiddenOperationException(string message) : base(message, 403)
    {
    }
}

public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string message)
        : base(message, 409)
    {
    }
}

public class AuthorizationExpiredException : PaymentException
{
    public AuthorizationExpiredException(string message) : base(message, 409)
    {
    }
}

public class PaymentGatewayException : PaymentException
{
    public string? PayPalDebugId { get; }
    public string? PayPalErrorName { get; }

    public PaymentGatewayException(
        string message,
        int statusCode = 502,
        string? payPalDebugId = null,
        string? payPalErrorName = null)
        : base(message, statusCode)
    {
        PayPalDebugId = payPalDebugId;
        PayPalErrorName = payPalErrorName;
    }
}
