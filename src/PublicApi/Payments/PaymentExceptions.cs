using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class PaymentException : Exception
{
    public PaymentException(
        string message,
        HttpStatusCode statusCode,
        string? providerCode = null,
        string? logRef = null,
        bool outcomeUnknown = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
        LogRef = logRef;
        OutcomeUnknown = outcomeUnknown;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ProviderCode { get; }
    public string? LogRef { get; }
    public bool OutcomeUnknown { get; }
}

public class PaymentNotFoundException : PaymentException
{
    public PaymentNotFoundException(string message)
        : base(message, HttpStatusCode.NotFound)
    {
    }
}

public class PaymentConflictException : PaymentException
{
    public PaymentConflictException(string message)
        : base(message, HttpStatusCode.Conflict)
    {
    }
}

public class PaymentValidationException : PaymentException
{
    public PaymentValidationException(string message)
        : base(message, HttpStatusCode.BadRequest)
    {
    }
}

public class PaymentProviderException : PaymentException
{
    public PaymentProviderException(
        string message,
        HttpStatusCode statusCode,
        string? providerCode = null,
        string? logRef = null,
        bool outcomeUnknown = false,
        Exception? innerException = null)
        : base(message, statusCode, providerCode, logRef, outcomeUnknown, innerException)
    {
    }
}
