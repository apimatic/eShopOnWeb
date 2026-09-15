using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string errorName, string message,
        string? debugId = null, string? issue = null)
        : base(message)
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

public sealed class PayPalPayerActionRequiredException : PayPalApiException
{
    public PayPalPayerActionRequiredException(string? debugId = null)
        : base(HttpStatusCode.Conflict, "PAYER_ACTION_REQUIRED",
            "PayPal requires browser approval for this card. This API intentionally does not implement an approval redirect.",
            debugId)
    {
    }
}
