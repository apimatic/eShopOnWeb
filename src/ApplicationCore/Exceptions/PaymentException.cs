using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public int StatusCode { get; }

    public PaymentException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }
}

public class PaymentNotFoundException : PaymentException
{
    public PaymentNotFoundException(string message) : base(message, 404)
    {
    }
}

public class PaymentForbiddenException : PaymentException
{
    public PaymentForbiddenException(string message) : base(message, 403)
    {
    }
}

public class PaymentConflictException : PaymentException
{
    public PaymentConflictException(string message) : base(message, 409)
    {
    }
}

public class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException(string message)
        : base(message, 409)
    {
    }
}

public class AuthorizationNotRenewableException : PaymentException
{
    public AuthorizationNotRenewableException(string message) : base(message, 409)
    {
    }
}

public class PaymentGatewayException : PaymentException
{
    public string? PayPalDebugId { get; }

    public PaymentGatewayException(string message, int statusCode = 502, string? payPalDebugId = null)
        : base(message, statusCode)
    {
        PayPalDebugId = payPalDebugId;
    }
}

public class PaymentConfigurationException : PaymentException
{
    public PaymentConfigurationException(string message) : base(message, 500)
    {
    }
}
