using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(string message, int statusCode = 400, string? errorCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }
    public string? ErrorCode { get; }
}

public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string message)
        : base(message, statusCode: 409, errorCode: "PAYER_ACTION_REQUIRED")
    {
    }
}

public class AuthorizationUnrenewableException : PaymentException
{
    public AuthorizationUnrenewableException(string message)
        : base(message, statusCode: 409, errorCode: "AUTHORIZATION_UNRENEWABLE")
    {
    }
}
