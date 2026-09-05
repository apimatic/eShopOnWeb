using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns an unsuccessful response.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public MaxioApiException(int statusCode, string responseBody)
        : base($"Maxio API request failed with status code {statusCode}: {responseBody}")
    {
        StatusCode = statusCode;
    }
}
