using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

/// <summary>
/// The single failure type the subscription integration presents to its callers. Every SDK failure
/// (typed API error, raw error, transport failure, or a malformed body) is translated to this at the
/// service boundary so endpoints have one type to map to an HTTP response — never a leaked
/// <c>System.Text.Json</c> or SDK type name.
/// </summary>
public sealed class MaxioIntegrationException : Exception
{
    /// <summary>
    /// The provider HTTP status when one is known (Case B raw errors, or a raw-error fallback on a
    /// typed error). Null for transport failures and unreadable bodies, where nothing answered.
    /// </summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// True when the failure is the caller's to fix (e.g. an invalid plan handle or a 4xx the caller
    /// can act on), as opposed to a provider/transport fault the caller cannot.
    /// </summary>
    public bool IsCallerError { get; }

    public MaxioIntegrationException(string message, HttpStatusCode? statusCode = null,
        bool isCallerError = false, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        IsCallerError = isCallerError;
    }
}
