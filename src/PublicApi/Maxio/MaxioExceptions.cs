using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thrown when the Maxio integration is not configured (missing or invalid settings).
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }

    public MaxioConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when a call to the Maxio API fails - either the transport layer or a non-success
/// HTTP status that the caller did not opt into handling.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(string message,
        HttpStatusCode? statusCode = null,
        IReadOnlyList<string>? errors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>HTTP status returned by Maxio, or null when the failure was at the transport layer.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>Validation/error strings returned by Maxio (when present in the response body).</summary>
    public IReadOnlyList<string> Errors { get; }
}
