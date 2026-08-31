using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string name, string message, string? issue, string? debugId)
        : base($"PayPal {name}: {message}" + (issue is null ? string.Empty : $" ({issue})") +
               (debugId is null ? string.Empty : $" [debug_id: {debugId}]"))
    {
        StatusCode = statusCode;
        ErrorName = name;
        Issue = issue;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string ErrorName { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
}

public sealed class PayPalChallengeRequiredException : Exception
{
    public PayPalChallengeRequiredException()
        : base("PayPal requires browser approval for this card. This headless integration cannot continue with that payment source.")
    {
    }
}
