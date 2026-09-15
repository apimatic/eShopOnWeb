using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string message, string? debugId = null,
        string? issue = null) : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        Issue = issue;
    }

    public HttpStatusCode StatusCode { get; }
    public string? DebugId { get; }
    public string? Issue { get; }
}

public sealed class PayPalChallengeRequiredException : PayPalApiException
{
    public PayPalChallengeRequiredException(string? debugId = null)
        : base(HttpStatusCode.Conflict,
            "PayPal requires browser approval for this card. This API supports only headless direct-card payments.",
            debugId, "PAYER_ACTION_REQUIRED") { }
}
