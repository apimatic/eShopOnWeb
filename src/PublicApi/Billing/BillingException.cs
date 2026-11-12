using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>A billing request the app cannot carry out; answered to the caller as a 4xx/5xx with the reason.</summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
