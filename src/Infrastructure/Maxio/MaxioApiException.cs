using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when a call to the Maxio Advanced Billing API fails or returns an unexpected result.
/// </summary>
public class MaxioApiException : Exception
{
    public int? StatusCode { get; }
    public string? ResponseBody { get; }

    public MaxioApiException(string message, int? statusCode = null, string? responseBody = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
