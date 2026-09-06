using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio Advanced Billing API. Error bodies follow the specification's
/// error models (<c>Error-List-Response</c>, <c>Customer-Error-Response</c>, <c>Error-*-Map-Response</c>),
/// all of which nest their content under an <c>errors</c> member.
/// </summary>
public class MaxioApiException : BillingProviderException
{
    public MaxioApiException(
        string operation,
        HttpStatusCode statusCode,
        IReadOnlyList<string> errors,
        string? rawBody = null,
        Exception? innerException = null)
        : base(BuildMessage(operation, statusCode, errors), IsClientErrorStatus(statusCode), errors, innerException)
    {
        Operation = operation;
        StatusCode = statusCode;
        RawBody = rawBody;
    }

    /// <summary>The specification <c>operationId</c> that failed, e.g. <c>createSubscription</c>.</summary>
    public string Operation { get; }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Raw response body, truncated. Logged for diagnostics; never returned to callers.</summary>
    public string? RawBody { get; }

    /// <summary>
    /// True when Maxio rejected a <c>reference</c> because another record already owns it. The
    /// reference is unique per site, which is what makes create operations safe to repeat.
    /// </summary>
    public bool IsReferenceTaken =>
        StatusCode == HttpStatusCode.UnprocessableEntity &&
        Errors.Any(e => e.Contains("Reference", StringComparison.OrdinalIgnoreCase) &&
                        e.Contains("taken", StringComparison.OrdinalIgnoreCase));

    /// <summary>True for 401/403 - almost always a bad or revoked API key.</summary>
    public bool IsAuthenticationFailure =>
        StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    private static bool IsClientErrorStatus(HttpStatusCode statusCode) =>
        (int)statusCode >= 400 && (int)statusCode < 500 &&
        statusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests);

    private static string BuildMessage(string operation, HttpStatusCode statusCode, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no error detail returned";
        return $"Maxio '{operation}' failed with HTTP {(int)statusCode} {statusCode}: {detail}";
    }
}
