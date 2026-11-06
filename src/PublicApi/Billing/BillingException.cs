using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Thrown when the app cannot carry out a billing request. Carries the HTTP status and the reason.
/// </summary>
public class BillingException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public BillingException(HttpStatusCode statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public static BillingException BadRequest(string message) => new(HttpStatusCode.BadRequest, message);
    public static BillingException NotFound(string message) => new(HttpStatusCode.NotFound, message);
    public static BillingException Conflict(string message) => new(HttpStatusCode.Conflict, message);
}
