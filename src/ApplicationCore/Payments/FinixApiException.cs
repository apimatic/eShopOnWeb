using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class FinixApiException : Exception
{
    public FinixApiException(int statusCode, string message, string? errorCode = null, string? logReference = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        LogReference = logReference;
    }

    public int StatusCode { get; }
    public string? ErrorCode { get; }
    public string? LogReference { get; }
}
