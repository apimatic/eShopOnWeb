using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }

    public PaymentException(string message, Exception innerException, int statusCode = 502)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
    public string? ProviderName { get; init; }
    public string? ProviderDebugId { get; init; }
    public bool ChallengeRequired { get; init; }
    public bool OutcomeUnknown { get; init; }
}
