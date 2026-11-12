using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The app cannot carry out a request. Turned into an HTTP status plus a reason by ExceptionMiddleware.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
