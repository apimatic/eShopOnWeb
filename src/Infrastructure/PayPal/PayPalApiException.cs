using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// An error response from the PayPal API, shaped by the error model in the PayPal
/// OpenAPI specifications (name / message / debug_id / details).
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string? errorName, string? debugId, string message)
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
