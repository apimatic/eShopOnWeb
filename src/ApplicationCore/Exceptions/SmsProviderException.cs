using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure talking to the messaging provider (a non-success API response, an unreadable response, or a
/// transport failure). Carries a caller-safe message and, where known, the provider's HTTP status so a
/// boundary can map a provider client-error (4xx) back to a client error and everything else to 5xx.
/// It deliberately never carries the auth token or a destination number.
/// </summary>
public class SmsProviderException : Exception
{
    /// <summary>The provider's HTTP status code, when one was returned; null for transport/parse failures.</summary>
    public int? ProviderStatusCode { get; }

    public SmsProviderException(string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    /// <summary>True when the provider itself rejected the request with a 4xx (the caller can act on it).</summary>
    public bool IsClientError => ProviderStatusCode is >= 400 and < 500;
}
