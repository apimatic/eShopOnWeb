using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The app cannot carry out a request; surfaces as a 4xx/5xx answer with the reason.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
