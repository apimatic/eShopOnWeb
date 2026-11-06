using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The app cannot carry out a request. Carries the HTTP status to answer with and the reason.
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
    public static BillingException Unprocessable(string message) => new(422, message);
}
