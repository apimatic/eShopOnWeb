using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the billing-provider boundary. <see cref="ProviderStatusCode"/> carries the
/// provider's HTTP status when one was received (4xx = the caller can act on it);
/// null means no meaningful provider status exists (transport failure, unreadable response).
/// The message is always caller-safe.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message, HttpStatusCode? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    public HttpStatusCode? ProviderStatusCode { get; }
}
