using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(string message) : base(message)
    {
    }

    public PaymentException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException(string message) : base(message)
    {
    }
}

public class AuthorizationCannotBeRenewedException : PaymentException
{
    public AuthorizationCannotBeRenewedException(string message) : base(message)
    {
    }
}

public class PaymentConflictException : PaymentException
{
    public PaymentConflictException(string message) : base(message)
    {
    }
}

public class OrderNotFoundException : PaymentException
{
    public OrderNotFoundException(string message) : base(message)
    {
    }
}

public class PaymentForbiddenException : PaymentException
{
    public PaymentForbiddenException(string message) : base(message)
    {
    }
}

public class PaymentDeclinedException : PaymentException
{
    public PaymentDeclinedException(string message) : base(message)
    {
    }
}

public class PayPalGatewayException : PaymentException
{
    public int? HttpStatus { get; }
    public string? PayPalErrorName { get; }
    public string? DebugId { get; }

    public PayPalGatewayException(string message, int? httpStatus = null, string? payPalErrorName = null, string? debugId = null)
        : base(message)
    {
        HttpStatus = httpStatus;
        PayPalErrorName = payPalErrorName;
        DebugId = debugId;
    }
}
