using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class PaymentOperationException : Exception
{
    public PaymentOperationException(string message, HttpStatusCode statusCode = HttpStatusCode.Conflict)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string name, string message, string? debugId)
        : base(message)
    {
        StatusCode = statusCode;
        Name = name;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string Name { get; }
    public string? DebugId { get; }
}

public sealed class PayPalPayerActionRequiredException : Exception
{
    public PayPalPayerActionRequiredException()
        : base("PayPal requires browser-based payer approval for this card; this API supports headless direct-card payments only.")
    {
    }
}
