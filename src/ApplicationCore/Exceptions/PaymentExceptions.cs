using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}

public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}

public class PaymentResourceNotFoundException : Exception
{
    public PaymentResourceNotFoundException(string message) : base(message) { }
}

public class FinixApiException : Exception
{
    public FinixApiException(
        int statusCode,
        string? code,
        string? logRef,
        string message,
        string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        LogRef = logRef;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }
    public string? Code { get; }
    public string? LogRef { get; }
    public string? ResponseBody { get; }
}
