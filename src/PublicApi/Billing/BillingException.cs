using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The app cannot carry out a billing request. Translated to an HTTP status + reason by ExceptionMiddleware.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string reason, Exception? inner = null) : base(reason, inner)
    {
        StatusCode = statusCode;
    }

    public static BillingException BadRequest(string reason) => new(400, reason);
    public static BillingException NotFound(string reason) => new(404, reason);
    public static BillingException Conflict(string reason) => new(409, reason);
}
