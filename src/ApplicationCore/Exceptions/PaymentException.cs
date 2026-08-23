using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public int StatusCode { get; }

    public PaymentException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }

    public PaymentException(string message, Exception inner, int statusCode = 400) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

public class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException(string message)
        : base(message, 409)
    {
    }
}

public class OrderPaymentStateException : PaymentException
{
    public OrderPaymentStateException(string message, int statusCode = 409) : base(message, statusCode)
    {
    }
}

public class AuthorizationUnrenewableException : PaymentException
{
    public AuthorizationUnrenewableException(string message) : base(message, 409)
    {
    }
}
