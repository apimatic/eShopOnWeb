using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the SMS gateway raises. It carries the provider's HTTP status when the
/// provider answered with an error (so the boundary can map a caller-fault 4xx back to the caller
/// and an our-fault/transport failure to a 5xx), and no status when nothing answered (transport
/// failure) or the answer could not be read.
/// </summary>
public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message, int? providerStatusCode, Exception? inner = null)
        : base(message, inner)
    {
        ProviderStatusCode = providerStatusCode;
    }

    public SmsGatewayException(string message, Exception? inner = null)
        : this(message, null, inner)
    {
    }

    /// <summary>Provider HTTP status, when the provider answered; null for transport/unreadable failures.</summary>
    public int? ProviderStatusCode { get; }
}
