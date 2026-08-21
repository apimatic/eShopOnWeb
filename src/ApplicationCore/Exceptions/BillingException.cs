using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Integration-boundary failure for the billing provider. <see cref="Exception.Message"/> is caller-safe.
/// </summary>
public class BillingException : Exception
{
    public BillingException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
