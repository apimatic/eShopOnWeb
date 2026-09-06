using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A call to the Maxio Billing API failed. Carries the upstream status and error messages so callers
/// can branch on documented outcomes (409 duplicate submission, 422 validation) and so operators get
/// something actionable in the logs.
/// </summary>
public class MaxioApiException : SubscriptionBillingException
{
    public MaxioApiException(string message, HttpMethod method, string requestPath,
        HttpStatusCode? statusCode = null, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, statusCode is null ? null : (int)statusCode, errors, innerException)
    {
        Method = method;
        RequestPath = requestPath;
        StatusCode = statusCode;
    }

    public HttpMethod Method { get; }

    /// <summary>Request path only — never the query string, which can carry customer references.</summary>
    public string RequestPath { get; }

    public HttpStatusCode? StatusCode { get; }
}
