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

public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string message)
        : base(message, 409)
    {
    }
}
