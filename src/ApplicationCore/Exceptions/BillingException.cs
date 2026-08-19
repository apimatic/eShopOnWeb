using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a billing-provider call fails or the request is invalid.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(string message, int statusCode = 502) : base(message)
    {
        StatusCode = statusCode;
    }

    public BillingException(string message, Exception innerException, int statusCode = 502)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
