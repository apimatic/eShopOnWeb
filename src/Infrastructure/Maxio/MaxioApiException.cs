using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns a non-success response.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, string responseBody)
        : base($"Maxio API returned HTTP {(int)statusCode}: {string.Join("; ", errors)}")
    {
        StatusCode = statusCode;
        Errors = errors;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public string ResponseBody { get; }
}

/// <summary>
/// Thrown when the Maxio configuration is missing or invalid.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
