using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Represents a non-success response from the Maxio Advanced Billing API.
/// Carries the upstream status code and the spec's error-list messages when present.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors, string? responseBody = null)
        : base(errors.Count > 0
            ? $"Maxio API error {(int)statusCode}: {string.Join("; ", errors)}"
            : $"Maxio API error {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        Errors = errors;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Error messages from the spec's Error-List-Response model (empty when unavailable).</summary>
    public IReadOnlyList<string> Errors { get; }

    public string? ResponseBody { get; }
}
