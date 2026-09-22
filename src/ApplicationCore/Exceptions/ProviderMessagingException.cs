using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at the Twilio wrapper boundary for any provider or transport failure. Carries the HTTP
/// status where the provider supplied one (null for transport/timeout failures, which answered
/// nothing). The message is caller-safe — it never echoes an SDK/framework exception message.
/// </summary>
public class ProviderMessagingException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public ProviderMessagingException(string message, Exception? inner = null)
        : base(message, inner) { }

    public ProviderMessagingException(string message, HttpStatusCode? statusCode, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
