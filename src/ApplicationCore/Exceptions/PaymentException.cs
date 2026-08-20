using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(string message, int statusCode = 400, string? errorCode = null)
        : base(message)
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
