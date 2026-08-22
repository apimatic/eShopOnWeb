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

public class PayPalPayerActionRequiredException : PaymentException
{
    public PayPalPayerActionRequiredException(string message)
        : base(message, 409)
    {
    }
}

public class AuthorizationCannotBeRenewedException : PaymentException
{
    public AuthorizationCannotBeRenewedException(string message)
        : base(message, 409)
    {
    }
}
