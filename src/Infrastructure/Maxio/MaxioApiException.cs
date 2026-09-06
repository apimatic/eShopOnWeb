using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio API, carrying the messages parsed out of the specification's
/// error models (<c>Error List Response</c>, <c>Error String Map</c>, <c>Error Array Map</c>,
/// <c>Single Error Response</c>).
/// </summary>
public class MaxioApiException : SubscriptionBillingException
{
    public MaxioApiException(
        HttpStatusCode statusCode,
        string method,
        string requestUri,
        IReadOnlyList<string> errors,
        string? rawBody)
        : base(BuildMessage(statusCode, method, requestUri, errors))
    {
        StatusCode = statusCode;
        Method = method;
        RequestUri = requestUri;
        Errors = errors;
        RawBody = rawBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string Method { get; }

    /// <summary>Relative request path. Never contains credentials.</summary>
    public string RequestUri { get; }

    public IReadOnlyList<string> Errors { get; }

    public string? RawBody { get; }

    /// <summary>True for statuses that indicate the caller sent something invalid rather than a server fault.</summary>
    public bool IsClientError => (int)StatusCode >= 400 && (int)StatusCode < 500;

    private static string BuildMessage(HttpStatusCode statusCode, string method, string requestUri, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join(" ", errors) : "no error detail returned";
        return $"Maxio API call {method} {requestUri} failed with status {(int)statusCode} ({statusCode}): {detail}";
    }
}
