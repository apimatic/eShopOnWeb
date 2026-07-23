using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the billing provider rejects, fails, or cannot serve a request. This is the single
/// typed error the provider seam surfaces, so no caller ever sees a provider-specific exception.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message) : base(message)
    {
    }

    public BillingProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public BillingProviderException(string message, int? statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status the provider returned, when one was available.</summary>
    public int? StatusCode { get; }
}
