using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PaymentOperationException : Exception
{
    public PaymentOperationException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed class PayPalPayerActionRequiredException : PaymentOperationException
{
    public PayPalPayerActionRequiredException(string debugId)
        : base(HttpStatusCode.Conflict,
            $"PayPal requires browser approval for this card. This API does not support an approval round-trip. PayPal debug ID: {debugId}") { }
}

public sealed class PayPalApiException : PaymentOperationException
{
    public PayPalApiException(HttpStatusCode statusCode, string message, string? debugId, string[] issues)
        : base(statusCode, message)
    {
        DebugId = debugId;
        Issues = issues;
    }

    public string? DebugId { get; }
    public string[] Issues { get; }
}
