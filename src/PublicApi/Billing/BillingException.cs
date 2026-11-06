using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Thrown when a billing request cannot be carried out. Rendered by the exception
/// middleware as an HTTP response with the given status code and reason.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
