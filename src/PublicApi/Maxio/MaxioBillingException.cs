using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>A request the app cannot carry out; surfaces as an HTTP status with a reason.</summary>
public class MaxioBillingException : Exception
{
    public int StatusCode { get; }

    public MaxioBillingException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
