using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// A request the app cannot carry out; carries the HTTP status and the reason returned to the caller.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
