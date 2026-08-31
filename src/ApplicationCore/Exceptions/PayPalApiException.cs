using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal rejected a call or returned an unexpected response. Carries PayPal's error name,
/// issue and debug id so operators can act on it. Maps to HTTP 502.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string? errorName, string? issue, string message, string? debugId = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        Issue = issue;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ErrorName { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
}
