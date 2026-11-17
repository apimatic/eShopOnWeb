using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentDomainException : Exception
{
    public PaymentDomainException(int statusCode, string message, string? code = null, string? logRef = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        LogRef = logRef;
    }

    public int StatusCode { get; }
    public string? Code { get; }
    public string? LogRef { get; }
}

public sealed class FinixProviderException : Exception
{
    public FinixProviderException(string message, int? statusCode = null, string? code = null, string? logRef = null,
        bool outcomeUnknown = false, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        LogRef = logRef;
        OutcomeUnknown = outcomeUnknown;
    }

    public int? StatusCode { get; }
    public string? Code { get; }
    public string? LogRef { get; }
    public bool OutcomeUnknown { get; }
}
