using System;

namespace Microsoft.eShopWeb.PublicApi.Finix;

public sealed class FinixProviderException : Exception
{
    public FinixProviderException(
        int? statusCode,
        string? code,
        string message,
        bool outcomeUnknown,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        OutcomeUnknown = outcomeUnknown;
    }

    public int? StatusCode { get; }

    public string? Code { get; }

    public bool OutcomeUnknown { get; }
}
