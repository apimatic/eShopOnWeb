using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at the Maxio SDK boundary. <see cref="ProviderStatusCode"/> carries the provider's
/// HTTP status when the failure came from an actual API response (so callers can distinguish a
/// rejected/invalid request from a provider outage); it is null for transport-level failures.
/// The message is always caller-safe - never an SDK/JSON exception message.
/// </summary>
public class MaxioIntegrationException : Exception
{
    public HttpStatusCode? ProviderStatusCode { get; }

    public MaxioIntegrationException(string message, HttpStatusCode? providerStatusCode = null)
        : base(message)
    {
        ProviderStatusCode = providerStatusCode;
    }

    public MaxioIntegrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
