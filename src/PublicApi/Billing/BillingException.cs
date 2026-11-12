using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>The app cannot carry out the request: becomes a 4xx/5xx with the reason as message.</summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public static BillingException BadRequest(string m) => new(400, m);
    public static BillingException NotFound(string m) => new(404, m);
    public static BillingException Conflict(string m) => new(409, m);
}
