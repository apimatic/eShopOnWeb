using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The app cannot carry out a billing request; carries the HTTP status to answer with and the reason.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
