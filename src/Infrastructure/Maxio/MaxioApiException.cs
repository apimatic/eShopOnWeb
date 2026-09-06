using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio Advanced Billing API, carrying the error messages
/// the specification's error models describe.
/// </summary>
public sealed class MaxioApiException : Exception
{
    public MaxioApiException(
        string operationId,
        HttpStatusCode statusCode,
        IReadOnlyList<string> errors,
        Exception? innerException = null)
        : base(BuildMessage(operationId, statusCode, errors), innerException)
    {
        OperationId = operationId;
        StatusCode = statusCode;
        Errors = errors;
    }

    /// <summary>The specification <c>operationId</c> of the call that failed.</summary>
    public string OperationId { get; }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Messages extracted from the response body, flattened across the spec's error shapes.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>True when the failure is on our side of the integration (credentials, permissions).</summary>
    public bool IsAuthenticationFailure =>
        StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    private static string BuildMessage(string operationId, HttpStatusCode statusCode, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0
            ? string.Join("; ", errors.Where(e => !string.IsNullOrWhiteSpace(e)))
            : "no error detail was returned";

        return $"Maxio operation '{operationId}' failed with status {(int)statusCode} ({statusCode}): {detail}";
    }
}
