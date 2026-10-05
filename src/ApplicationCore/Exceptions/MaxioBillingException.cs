using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised by <see cref="Interfaces.IMaxioBillingService"/> when a Maxio Advanced Billing call fails.
/// Carries a caller-safe message and, when the provider answered, the provider's HTTP status —
/// never the raw SDK exception text.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingException(string message, HttpStatusCode? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    /// <summary>
    /// The HTTP status the provider returned, when it answered at all. Null means a transport-level
    /// failure (the provider may or may not have acted).
    /// </summary>
    public HttpStatusCode? ProviderStatusCode { get; }
}