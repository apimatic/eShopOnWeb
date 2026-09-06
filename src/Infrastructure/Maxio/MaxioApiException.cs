using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio API, with the error messages decoded from whichever of the
/// specification's error shapes the endpoint returned.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpMethod method, string requestPath, HttpStatusCode statusCode,
        IEnumerable<string>? errors = null, Exception? innerException = null)
        : base(BuildMessage(method, requestPath, statusCode, errors), innerException)
    {
        Method = method.Method;
        RequestPath = requestPath;
        StatusCode = statusCode;
        Errors = errors?.ToList() ?? new List<string>();
    }

    public string Method { get; }

    public string RequestPath { get; }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when Maxio rejected a create because the reference eShopOnWeb supplied is already taken,
    /// e.g. <c>"Reference: must be unique - that value has been taken."</c>.
    /// </summary>
    public bool IsReferenceConflict =>
        StatusCode == HttpStatusCode.UnprocessableEntity &&
        Errors.Any(e =>
            e.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
            (e.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
             e.Contains("has been taken", StringComparison.OrdinalIgnoreCase)));

    private static string BuildMessage(HttpMethod method, string requestPath, HttpStatusCode statusCode,
        IEnumerable<string>? errors)
    {
        var detail = errors is null ? null : string.Join("; ", errors);
        var summary = $"Maxio request {method.Method} {requestPath} failed with status {(int)statusCode} {statusCode}.";
        return string.IsNullOrWhiteSpace(detail) ? summary : $"{summary} {detail}";
    }
}

/// <summary>Raised when the Maxio API could not be reached or did not answer in time.</summary>
public class MaxioTransportException : Exception
{
    public MaxioTransportException(HttpMethod method, string requestPath, Exception innerException)
        : base($"Maxio request {method.Method} {requestPath} could not be completed: {innerException.Message}", innerException)
    {
        Method = method.Method;
        RequestPath = requestPath;
    }

    public string Method { get; }

    public string RequestPath { get; }
}
