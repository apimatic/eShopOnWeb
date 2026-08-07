using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Raised when a PayPal API call fails in a way that is not an expected card decline (which is surfaced
/// as a failed result instead). Carries PayPal's debug id and error name to aid diagnosis. Never
/// contains card data.
/// </summary>
public class PayPalApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? DebugId { get; }
    public string? PayPalErrorName { get; }

    public PayPalApiException(string message, HttpStatusCode statusCode, string? debugId, string? payPalErrorName)
        : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        PayPalErrorName = payPalErrorName;
    }
}
