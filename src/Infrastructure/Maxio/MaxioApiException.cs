using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when Maxio returns an unsuccessful HTTP response. Carries the status code and any error
/// messages Maxio provided so callers can react (e.g. treat 409/422 as a duplicate-submission race).
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors, string requestDescription)
        : base(BuildMessage(statusCode, errors, requestDescription))
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public bool IsConflict => StatusCode == HttpStatusCode.Conflict;

    public bool IsUnprocessable => StatusCode == HttpStatusCode.UnprocessableEntity;

    private static string BuildMessage(HttpStatusCode statusCode, IReadOnlyList<string> errors, string requestDescription)
    {
        var detail = errors is { Count: > 0 } ? string.Join("; ", errors) : "(no error detail)";
        return $"Maxio request '{requestDescription}' failed with {(int)statusCode} {statusCode}: {detail}";
    }
}
