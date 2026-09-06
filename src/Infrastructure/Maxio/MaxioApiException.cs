using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio Advanced Billing API.
/// </summary>
public sealed class MaxioApiException : Exception
{
    public MaxioApiException(
        string operation,
        HttpStatusCode statusCode,
        IReadOnlyList<string> errors,
        string? rawBody,
        TimeSpan? retryAfter = null)
        : base(BuildMessage(operation, statusCode, errors))
    {
        Operation = operation;
        StatusCode = statusCode;
        Errors = errors;
        RawBody = rawBody;
        RetryAfter = retryAfter;
    }

    /// <summary>The spec operationId that failed, e.g. "createSubscription".</summary>
    public string Operation { get; }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Messages extracted from the spec's error schemas, best effort.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Response body, truncated. Never contains credentials: only the request carries those.</summary>
    public string? RawBody { get; }

    public TimeSpan? RetryAfter { get; }

    /// <summary>True for statuses the caller can fix by changing the request.</summary>
    public bool IsValidationFailure =>
        StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity;

    private static string BuildMessage(string operation, HttpStatusCode statusCode, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no error detail returned";
        return $"Maxio operation '{operation}' failed with status {(int)statusCode} ({statusCode}): {detail}";
    }
}
