using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when the Maxio integration is not configured.
/// </summary>
public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when a Maxio Advanced Billing API call returns a non-success HTTP status.
/// Carries the upstream status code and raw response body for diagnostics.
/// </summary>
public sealed class MaxioApiException : Exception
{
    public MaxioApiException(string message, int statusCode, string? responseBody, string requestUri)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        RequestUri = requestUri;
    }

    public int StatusCode { get; }

    public string? ResponseBody { get; }

    public string RequestUri { get; }
}
