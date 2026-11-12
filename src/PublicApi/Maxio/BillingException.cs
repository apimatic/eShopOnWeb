using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The one failure type the subscription service exposes. It carries a caller-safe message and the HTTP status the
/// API should answer with; SDK/provider details never leave the service.
/// </summary>
public class BillingException : Exception
{
    public BillingException(HttpStatusCode statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }

    public static BillingException BadRequest(string message, Exception? inner = null) => new(HttpStatusCode.BadRequest, message, inner);
    public static BillingException Conflict(string message) => new(HttpStatusCode.Conflict, message);
    public static BillingException Unavailable(string message, Exception? inner = null) => new(HttpStatusCode.ServiceUnavailable, message, inner);
    public static BillingException BadGateway(string message, Exception? inner = null) => new(HttpStatusCode.BadGateway, message, inner);
}
