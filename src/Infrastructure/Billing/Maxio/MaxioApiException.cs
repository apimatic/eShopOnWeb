using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// A non-success response from Advanced Billing, or a failure to reach it at all.
/// </summary>
public class MaxioApiException : Exception
{
    /// <summary>
    /// Fragment Advanced Billing uses when a caller-assigned reference is already taken. The API
    /// enforces uniqueness on both customer and subscription references, which is what turns a
    /// duplicate submit into a recoverable conflict instead of a duplicate record.
    /// </summary>
    private const string ReferenceTakenMarker = "must be unique";

    public MaxioApiException(
        string message,
        string method,
        string requestPath,
        HttpStatusCode? statusCode = null,
        IEnumerable<string>? errors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Method = method;
        RequestPath = requestPath;
        StatusCode = statusCode;
        Errors = errors?.ToList() ?? new List<string>();
    }

    /// <summary>HTTP method of the failing call.</summary>
    public string Method { get; }

    /// <summary>Path of the failing call. Never includes credentials.</summary>
    public string RequestPath { get; }

    /// <summary>Status the billing system returned, or null when the call never reached it.</summary>
    public HttpStatusCode? StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    /// <summary>True when the request was rejected because a reference we supplied already exists.</summary>
    public bool IsReferenceConflict =>
        StatusCode == HttpStatusCode.UnprocessableEntity
        && Errors.Any(e => e.Contains(ReferenceTakenMarker, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the same request has a reasonable chance of succeeding later.</summary>
    public bool IsTransient =>
        StatusCode is null // never reached the billing system (network error / timeout)
        || StatusCode == HttpStatusCode.TooManyRequests
        || (int)StatusCode >= 500;

    /// <summary>True when the billing system rejected our credentials or refused the operation.</summary>
    public bool IsAuthFailure =>
        StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
