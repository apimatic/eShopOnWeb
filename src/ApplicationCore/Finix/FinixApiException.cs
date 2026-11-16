using System;

namespace Microsoft.eShopWeb.ApplicationCore.Finix;

public class FinixApiException : Exception
{
    public FinixApiException(int statusCode, string message, string? errorCode = null,
        string? duplicateResourceId = null, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        DuplicateResourceId = duplicateResourceId;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }
    public string? ErrorCode { get; }
    public string? DuplicateResourceId { get; }
    public string? ResponseBody { get; }
}
