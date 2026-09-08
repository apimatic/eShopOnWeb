using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when the Maxio integration is used before the required configuration is available.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// Raised when the Maxio Billing API returns an error response. Carries the HTTP status code and
/// the error message(s) returned by Maxio.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status code returned by the Billing API.</summary>
    public int StatusCode { get; }
}
