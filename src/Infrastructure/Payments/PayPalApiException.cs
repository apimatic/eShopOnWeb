using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string message, string? debugId,
        string? issue) : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        Issue = issue;
    }

    public HttpStatusCode StatusCode { get; }
    public string? DebugId { get; }
    public string? Issue { get; }
}
