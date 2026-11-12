using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>A request the app cannot carry out; the middleware answers with <see cref="StatusCode"/> and the message.</summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
