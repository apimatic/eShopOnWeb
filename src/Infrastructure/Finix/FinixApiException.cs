using System;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

public class FinixApiException : Exception
{
    public FinixApiException(int statusCode, string? errorCode, string message, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }
    public string? ErrorCode { get; }
    public string? ResponseBody { get; }
}
