using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Raised when the app cannot carry out a billing request. Surfaces as a 4xx (or 5xx for
/// upstream/config problems) response carrying the reason.
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
