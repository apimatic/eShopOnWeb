using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The application can't carry out a request: carries the HTTP status and a reason for the caller.
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
    public static BillingException Unprocessable(string message) => new(HttpStatusCode.UnprocessableEntity, message);
}
