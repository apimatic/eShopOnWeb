using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at the messaging-provider boundary when a provider call fails (an API error, an
/// unreadable response, or the provider being unreachable). The message is caller-safe — it
/// never carries a phone number, the auth token, or a raw provider payload. Maps to HTTP 502.
/// </summary>
public class NotificationProviderException : Exception
{
    /// <summary>The HTTP status the provider returned, when the failure came from a provider response.</summary>
    public HttpStatusCode? ProviderStatusCode { get; }

    public NotificationProviderException(string message, HttpStatusCode? providerStatusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        ProviderStatusCode = providerStatusCode;
    }
}
