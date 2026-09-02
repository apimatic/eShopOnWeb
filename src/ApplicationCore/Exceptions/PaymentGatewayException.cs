using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The payment gateway rejected or failed a call. Carries the gateway's error name and
/// correlation id so operators can follow up with the provider.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(HttpStatusCode statusCode, string? errorName, string message, string? debugId = null)
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
