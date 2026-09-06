using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A Maxio API call failed. Carries the operation, the HTTP status and the messages parsed out of
/// the error envelope so callers can decide between retrying, failing the request, or reporting a
/// validation problem back to the shopper.
/// </summary>
public class MaxioApiException : SubscriptionBillingException
{
    public MaxioApiException(
        string operationId,
        HttpStatusCode statusCode,
        IReadOnlyList<string> errors,
        string? responseBody = null,
        Exception? innerException = null)
        : base(BuildMessage(operationId, statusCode, errors), innerException!)
    {
        OperationId = operationId;
        StatusCode = statusCode;
        Errors = errors;
        ResponseBody = responseBody;
    }

    /// <summary>The specification operationId of the call that failed, e.g. "createSubscription".</summary>
    public string OperationId { get; }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    /// <summary>Raw response body, retained for diagnostics. Never contains credentials.</summary>
    public string? ResponseBody { get; }

    private static string BuildMessage(string operationId, HttpStatusCode statusCode, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no error detail was returned";
        return $"Maxio operation '{operationId}' failed with status {(int)statusCode} ({statusCode}): {detail}.";
    }
}

/// <summary>
/// Maxio could not be reached at all, or did not answer in time. Distinct from
/// <see cref="MaxioApiException"/> because no request was necessarily processed.
/// </summary>
public class MaxioTransportException : SubscriptionBillingException
{
    public MaxioTransportException(string operationId, string message, Exception innerException)
        : base($"Maxio operation '{operationId}' could not be completed: {message}", innerException)
    {
        OperationId = operationId;
    }

    public string OperationId { get; }
}
