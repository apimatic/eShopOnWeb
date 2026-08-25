using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

// Wraps a non-success response from PayPal so callers can surface PayPal's own
// error name/message/issues to an operator instead of a generic failure.
public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string? errorName, string message, string? debugId)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ErrorName { get; }
    public string? DebugId { get; }
}

// PayPal responded that the buyer must complete an additional action (e.g. a 3DS
// challenge) in a browser before the payment can proceed. This integration is direct-card
// only and does not implement a buyer approval round-trip, so this must surface as a hard
// failure rather than being worked around.
public class PayPalApprovalRequiredException : Exception
{
    public PayPalApprovalRequiredException(string message) : base(message) { }
}
