using System;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// The app cannot carry out a billing request. Carries the HTTP status and the reason given to the caller.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
