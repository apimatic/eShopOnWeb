using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string message, string? issue, string? debugId)
        : base(message)
    {
        StatusCode = statusCode;
        Issue = issue;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
}

public sealed class PayPalChallengeRequiredException : Exception
{
    public PayPalChallengeRequiredException()
        : base("PayPal requires browser approval for this card. No headless approval round-trip was attempted.") { }
}
