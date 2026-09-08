using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

/// <summary>
/// Raised when the Maxio integration has not been configured (missing API key / base URL /
/// product family). This is an operational misconfiguration, not a caller error.
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
/// Raised when the Maxio API returns a non-success status code.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string requestUri, IReadOnlyList<string> errors)
        : base(BuildMessage(statusCode, requestUri, errors))
    {
        StatusCode = statusCode;
        RequestUri = requestUri;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }

    public string RequestUri { get; }

    /// <summary>
    /// The flattened list of error messages returned by the API (from its error model).
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(HttpStatusCode statusCode, string requestUri, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join(" | ", errors) : "No error detail returned.";
        return $"Maxio API request '{requestUri}' failed with HTTP {(int)statusCode} ({statusCode}): {detail}";
    }
}
