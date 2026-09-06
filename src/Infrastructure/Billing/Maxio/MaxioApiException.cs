using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// A non-success response from the Maxio API. <see cref="Errors"/> holds the messages Maxio returned
/// in the specification's error schemas (<c>Error List Response</c>, <c>Customer Error Response</c>).
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(
        HttpMethod method,
        string requestPath,
        HttpStatusCode statusCode,
        IReadOnlyList<string> errors)
        : base(BuildMessage(method, requestPath, statusCode, errors))
    {
        Method = method;
        RequestPath = requestPath;
        StatusCode = statusCode;
        Errors = errors;
    }

    public HttpMethod Method { get; }

    public string RequestPath { get; }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when Maxio rejected the request because the <c>reference</c> is already taken. References
    /// are unique per site, which is what makes enrollment safe to retry.
    /// </summary>
    public bool IsDuplicateReference =>
        StatusCode == HttpStatusCode.UnprocessableEntity &&
        Errors.Any(e =>
            e.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
            e.Contains("unique", StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(
        HttpMethod method,
        string requestPath,
        HttpStatusCode statusCode,
        IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no error detail was returned";
        return $"Maxio returned {(int)statusCode} {statusCode} for {method} {requestPath}: {detail}.";
    }
}
