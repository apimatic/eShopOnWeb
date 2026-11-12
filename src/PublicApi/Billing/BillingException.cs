using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The app cannot carry out a request. Surfaces as a 4xx/5xx status with the message as the reason.
/// </summary>
public class BillingException : Exception
{
    public BillingException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }

    public static BillingException NotFound(string message) => new(HttpStatusCode.NotFound, message);
    public static BillingException BadRequest(string message) => new(HttpStatusCode.BadRequest, message);
    public static BillingException Conflict(string message) => new(HttpStatusCode.Conflict, message);
    public static BillingException UnprocessableEntity(string message) => new(HttpStatusCode.UnprocessableEntity, message);
}
