using System;

namespace Microsoft.eShopWeb.ApplicationCore.Finix;

public class FinixApiException : Exception
{
    public FinixApiException(int statusCode, string message, string? code = null, string? logReference = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        LogReference = logReference;
    }

    public int StatusCode { get; }
    public string? Code { get; }
    public string? LogReference { get; }
}
