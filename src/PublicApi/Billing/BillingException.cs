using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>A request the application cannot carry out: a status and a caller-safe reason.</summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
