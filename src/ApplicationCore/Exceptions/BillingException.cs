using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Failure at the billing-provider boundary. Message is always caller-safe
/// (provider internals are on <see cref="Exception.InnerException"/>, never here).
/// </summary>
public class BillingException : Exception
{
    public BillingException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// HTTP status the API should answer with. Provider 4xx are carried through;
    /// transport/unknown failures surface as 5xx.
    /// </summary>
    public int StatusCode { get; }
}
