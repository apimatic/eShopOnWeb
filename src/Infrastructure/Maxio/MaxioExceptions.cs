using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when the Maxio Advanced Billing configuration is missing or invalid.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when the Maxio Advanced Billing API responds with a non-success status code.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors, string requestPath)
        : base($"Maxio API request to '{requestPath}' failed with HTTP {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        Errors = errors;
        RequestPath = requestPath;
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public string RequestPath { get; }
}
