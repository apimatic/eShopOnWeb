using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system could not complete a request. <see cref="Exception.Message"/> is safe to return to API
/// callers; provider details stay on <see cref="Exception.InnerException"/> for logs only.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(int statusCode, string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderStatusCode = providerStatusCode;
    }

    /// <summary>The HTTP status to return to our caller.</summary>
    public int StatusCode { get; }

    /// <summary>The HTTP status the billing system answered with, when it answered at all.</summary>
    public int? ProviderStatusCode { get; }
}
