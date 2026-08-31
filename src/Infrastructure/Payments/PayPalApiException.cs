using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string issue, string message, string? debugId)
        : base(message)
    {
        StatusCode = statusCode;
        Issue = issue;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string Issue { get; }
    public string? DebugId { get; }
}
