using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message)
    {
    }
}

public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message)
    {
    }
}

public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message)
    {
    }
}

public class FinixGatewayException : Exception
{
    public FinixGatewayException(string message, HttpStatusCode statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
