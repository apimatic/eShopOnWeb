using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string errorName, string message,
        string? debugId, string? issue = null) : base(message)
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
}

public sealed class PayPalPayerActionRequiredException : Exception
{
    public PayPalPayerActionRequiredException()
        : base("PayPal requires browser-based payer approval for this card. This headless payment flow cannot continue.") { }
}
