using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Raised when the app cannot carry out a billing request. Rendered as a 4xx (or 5xx for
/// upstream problems) response carrying the reason.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
