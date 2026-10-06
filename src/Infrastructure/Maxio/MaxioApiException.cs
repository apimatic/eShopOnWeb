using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio Billing API returns an unexpected response.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public MaxioApiException(string message, int statusCode, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}