using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when subscription billing cannot complete. <see cref="StatusCode"/> is the HTTP
/// status that PublicApi should return to the caller.
/// </summary>
public class BillingException : Exception
{
    public BillingException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public BillingException(int statusCode, string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
