using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The app can't carry out a billing request; surfaces as an HTTP status with a reason.
/// </summary>
public class BillingException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public BillingException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public static BillingException BadRequest(string message) => new(HttpStatusCode.BadRequest, message);
    public static BillingException NotFound(string message) => new(HttpStatusCode.NotFound, message);
}
