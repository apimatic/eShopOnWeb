using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>An error that is reported to the API caller with the given HTTP status and reason.</summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
