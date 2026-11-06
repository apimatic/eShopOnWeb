using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Raised when the app cannot carry out a billing request. Carries the HTTP status and the reason
/// to give the caller.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public static BillingException BadRequest(string message) => new(400, message);
    public static BillingException NotFound(string message) => new(404, message);
    public static BillingException Conflict(string message) => new(409, message);
}
