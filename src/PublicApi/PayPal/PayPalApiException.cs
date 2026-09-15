using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string errorName, string safeMessage,
        string? debugId, string? issue = null)
        : base(safeMessage)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        DebugId = debugId;
        Issue = issue;
    }

    public HttpStatusCode StatusCode { get; }
    public string ErrorName { get; }
    public string? DebugId { get; }
    public string? Issue { get; }

    public bool IsAuthorizationStale =>
        Issue is "AUTHORIZATION_EXPIRED" or "AUTHORIZATION_VOIDED" ||
        ErrorName is "AUTHORIZATION_EXPIRED";
}

public sealed class PayPalPayerActionRequiredException : Exception
{
    public PayPalPayerActionRequiredException()
        : base("PayPal required an interactive cardholder challenge. This headless integration cannot continue this payment.") { }
}
