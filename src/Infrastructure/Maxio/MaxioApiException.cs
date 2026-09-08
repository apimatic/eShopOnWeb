using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns an unexpected (non-2xx) response.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }
    public string ResponseBody { get; }
}
