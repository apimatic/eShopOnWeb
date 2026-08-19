using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Failure at the billing integration boundary. <see cref="StatusCode"/> is the HTTP status
/// the PublicApi should return; <see cref="Exception.Message"/> is already caller-safe.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message, int statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
