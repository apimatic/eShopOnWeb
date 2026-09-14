using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns a non-success response.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public string ResponseBody { get; }

    public MaxioApiException(int statusCode, string responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
