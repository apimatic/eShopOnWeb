using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when the Maxio configuration is incomplete. Contains no secret values.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message) { }
}

/// <summary>
/// Represents a non-success response from the Maxio Billing API.
/// The response body is retained for diagnostics but must never contain secrets.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public string? ResponseBody { get; }

    public MaxioApiException(int statusCode, string? responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
