using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class PayPalException : Exception
{
    public PayPalException(HttpStatusCode statusCode, string errorName, string message, string? issue, string? debugId)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        Issue = issue;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string ErrorName { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
}

public sealed class PaymentActionRequiredException : Exception
{
    public PaymentActionRequiredException()
        : base("PayPal requires an interactive payer challenge. This API intentionally does not implement a browser approval round-trip; use another card or contact PayPal support.")
    {
    }
}

public sealed class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message) { }
}
