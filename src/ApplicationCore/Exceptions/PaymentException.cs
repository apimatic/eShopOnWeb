using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

public class InvalidOrderStateException : PaymentException
{
    public InvalidOrderStateException(string message) : base(message, 409)
    {
    }
}

public class RefundLimitException : PaymentException
{
    public RefundLimitException(string message) : base(message, 409)
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

public class StaleAuthorizationException : PaymentException
{
    public StaleAuthorizationException(string message) : base(message, 409)
    {
    }
}

public class OrderNotFoundException : PaymentException
{
    public OrderNotFoundException(int orderId) : base($"Order {orderId} was not found.", 404)
    {
    }
}

public class PaymentMethodNotFoundException : PaymentException
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"Payment method {paymentMethodId} was not found.", 404)
    {
    }
}

public class ForbiddenOrderAccessException : PaymentException
{
    public ForbiddenOrderAccessException() : base("You are not allowed to access this order.", 403)
    {
    }
}
