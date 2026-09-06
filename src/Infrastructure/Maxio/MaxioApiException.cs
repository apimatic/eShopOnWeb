using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio Advanced Billing API.
/// </summary>
public class MaxioApiException : Exception
{
    private const string DuplicateReferenceMarker = "must be unique";
    private const string ValueTakenMarker = "has been taken";

    public MaxioApiException(
        HttpMethod method,
        string requestUri,
        HttpStatusCode statusCode,
        IReadOnlyList<string> errors,
        string? rawBody)
        : base(BuildMessage(method, requestUri, statusCode, errors))
    {
        Method = method;
        RequestUri = requestUri;
        StatusCode = statusCode;
        Errors = errors;
        RawBody = rawBody;
    }

    public HttpMethod Method { get; }

    public string RequestUri { get; }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Validation messages Maxio returned, in the order it returned them.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Raw response body, truncated for logging.</summary>
    public string? RawBody { get; }

    /// <summary>
    /// True when Maxio rejected the request because a <c>reference</c> we supplied is already
    /// taken - the signal that another request (or an earlier one) already created the record.
    /// </summary>
    public bool IsDuplicateReference =>
        StatusCode == HttpStatusCode.UnprocessableEntity &&
        Errors.Any(e =>
            e.Contains(DuplicateReferenceMarker, StringComparison.OrdinalIgnoreCase) ||
            e.Contains(ValueTakenMarker, StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(HttpMethod method, string requestUri, HttpStatusCode statusCode, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : statusCode.ToString();
        return $"Maxio responded {(int)statusCode} to {method} {requestUri}: {detail}";
    }
}

