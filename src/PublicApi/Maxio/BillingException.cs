using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>A request the app cannot carry out. StatusCode is the status the endpoint answers with.</summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
