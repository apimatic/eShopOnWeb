using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio API returns a non-success response. Carries the HTTP status and any
/// error messages parsed from the <c>{ "errors": [...] }</c> body.
/// </summary>
internal sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors, string requestSummary)
        : base(BuildMessage(statusCode, errors, requestSummary))
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(HttpStatusCode statusCode, IReadOnlyList<string> errors, string requestSummary)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "(no error detail)";
        return $"Maxio API request '{requestSummary}' failed with {(int)statusCode} {statusCode}: {detail}";
    }
}
