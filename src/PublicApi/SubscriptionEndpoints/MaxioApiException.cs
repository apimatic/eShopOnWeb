using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns an error response.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public MaxioApiException(int statusCode, string responseBody)
        : base($"Maxio API request failed with status {(int)statusCode} ({statusCode}). Body: {responseBody}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
