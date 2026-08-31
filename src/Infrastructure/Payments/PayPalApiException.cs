using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string errorName, string message, string? debugId,
        string? issue)
        : base($"PayPal {errorName}: {message}" +
            (string.IsNullOrWhiteSpace(issue) ? string.Empty : $" ({issue})") +
            (string.IsNullOrWhiteSpace(debugId) ? string.Empty : $" [debug_id: {debugId}]"))
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
