using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The payment gateway (PayPal) rejected or failed a call. Carries PayPal's error name and
/// debug id for support correlation. Never carries card data.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(HttpStatusCode statusCode, string? errorName, string message, string? debugId)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ErrorName { get; }
    public string? DebugId { get; }
}
