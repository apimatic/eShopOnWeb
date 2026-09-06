using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Billing.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// A non-success response from the Maxio API, carrying the operation that failed, the HTTP status
/// and the messages parsed from the error models the specification defines
/// (<c>Error-List-Response</c>, <c>Customer-Error-Response</c>, <c>Error-Array-Map-Response</c>).
/// </summary>
public sealed class MaxioApiException : BillingProviderException
{
    public MaxioApiException(
        string operation,
        HttpStatusCode statusCode,
        IReadOnlyList<string> errors,
        Exception? innerException = null)
        : base(BuildMessage(operation, statusCode, errors), (int)statusCode, errors, innerException)
    {
        Operation = operation;
    }

    /// <summary>The specification <c>operationId</c> that failed, e.g. <c>createSubscription</c>.</summary>
    public string Operation { get; }

    private static string BuildMessage(string operation, HttpStatusCode statusCode, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0
            ? string.Join("; ", errors)
            : ReasonFor(statusCode);

        return $"Maxio operation '{operation}' failed with status {(int)statusCode} ({statusCode}): {detail}";
    }

    private static string ReasonFor(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "the Maxio API key was rejected.",
        HttpStatusCode.Forbidden => "the Maxio API key is not permitted to perform this operation.",
        HttpStatusCode.NotFound => "the requested Maxio resource does not exist.",
        (HttpStatusCode)429 => "the Maxio API rate limit was exceeded.",
        _ => "no error detail was returned."
    };
}
