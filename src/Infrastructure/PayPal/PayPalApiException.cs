using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// An error returned by the PayPal API. Carries PayPal's own error name and
/// debug id so operators can act on it. Never contains card data.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string? errorName, string message, string? debugId)
        : base($"PayPal error {(int)statusCode} {errorName}: {message}" + (debugId != null ? $" (debug id: {debugId})" : ""))
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ErrorName { get; }
    public string? DebugId { get; }
}
