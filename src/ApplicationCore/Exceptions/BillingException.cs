using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the billing-provider boundary. <see cref="StatusCode"/> is the HTTP status
/// the API should surface; <see cref="Exception.Message"/> is always caller-safe (provider
/// bodies and framework exception details are logged server-side, never relayed).
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
