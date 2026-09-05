using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A translated failure from the Maxio billing provider. <see cref="StatusCode"/> is the HTTP
/// status the caller of our API should see: a provider 4xx is carried through as the matching
/// client 4xx, while a transport failure, an unreadable response, or any other provider status
/// is carried through as 502/503 - never surface the underlying SDK exception message.
/// </summary>
public class MaxioIntegrationException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioIntegrationException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
