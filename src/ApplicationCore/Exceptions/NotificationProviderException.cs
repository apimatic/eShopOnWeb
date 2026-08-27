using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Single failure type for every messaging-provider error (API rejection, transport
/// failure, unprocessable response). Carries the provider's HTTP status when one exists.
/// The message is always caller-safe: it never contains shopper phone numbers or secrets.
/// </summary>
public class NotificationProviderException : Exception
{
    public NotificationProviderException(string message, HttpStatusCode? providerStatusCode = null,
        int? providerErrorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        ProviderErrorCode = providerErrorCode;
    }

    public HttpStatusCode? ProviderStatusCode { get; }
    public int? ProviderErrorCode { get; }
}
