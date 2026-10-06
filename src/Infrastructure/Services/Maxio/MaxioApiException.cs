using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Thrown when the Maxio API returns a non-success status code.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public MaxioApiException(int statusCode, string responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public string ResponseBody { get; }
}
