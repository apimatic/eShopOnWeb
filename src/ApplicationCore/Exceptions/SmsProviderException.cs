using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the SMS provider boundary. The message is caller-safe (no credentials,
/// no destination numbers); the provider's HTTP status is carried when one exists.
/// </summary>
public class SmsProviderException : Exception
{
    public SmsProviderException(string message, HttpStatusCode? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    public HttpStatusCode? ProviderStatusCode { get; }
}
