using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Raised when a PayPal API call returns an error. Carries the HTTP status plus the PayPal error
/// name / message / debug_id parsed from the spec's <c>error</c> model, for diagnosis. The raw
/// request body (which may contain card data) is never included.
/// </summary>
public class PayPalApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? PayPalErrorName { get; }
    public string? DebugId { get; }

    public PayPalApiException(HttpStatusCode statusCode, string? payPalErrorName, string message, string? debugId)
        : base(message)
    {
        StatusCode = statusCode;
        PayPalErrorName = payPalErrorName;
        DebugId = debugId;
    }
}
