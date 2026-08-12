using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Single failure type raised by <see cref="ISmsGateway"/> for any provider or transport error, so callers
/// do not have to know about the underlying SDK's exception shapes. Carries the provider's HTTP status when
/// one was available, so a caller can tell a deterministic rejection (4xx) from an outage (5xx / transport).
/// The message is always caller-safe and never contains a phone number or the auth token.
/// </summary>
public class SmsGatewayException : Exception
{
    /// <summary>The provider's HTTP status code, when the failure carried one; null for transport failures.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>True when the provider deterministically rejected the request (a 4xx), so a retry cannot help.</summary>
    public bool IsProviderRejection => ProviderStatusCode is >= 400 and < 500;

    public SmsGatewayException(string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }
}
