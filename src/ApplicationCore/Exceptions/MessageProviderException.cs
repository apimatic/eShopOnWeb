using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the messaging provider boundary. The message is always caller-safe:
/// it never contains credentials or destination phone numbers.
/// </summary>
public class MessageProviderException : Exception
{
    public HttpStatusCode? ProviderStatusCode { get; }

    public MessageProviderException(string message, HttpStatusCode? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }
}
