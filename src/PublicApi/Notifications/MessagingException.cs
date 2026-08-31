using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

/// <summary>
/// The single failure type leaving the Twilio integration boundary. Carries the provider's
/// HTTP status when there was one. The message is always caller-safe: provider error bodies
/// may contain destination phone numbers and are never copied here.
/// </summary>
public sealed class MessagingException : Exception
{
    public MessagingException(string message, HttpStatusCode? providerStatusCode, Exception? inner = null)
        : base(message, inner)
    {
        ProviderStatusCode = providerStatusCode;
    }

    public HttpStatusCode? ProviderStatusCode { get; }
}
