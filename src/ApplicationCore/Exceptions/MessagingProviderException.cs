using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MessagingProviderException : Exception
{
    public MessagingProviderException(string message, HttpStatusCode? providerStatusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        ProviderStatusCode = providerStatusCode;
    }

    public HttpStatusCode? ProviderStatusCode { get; }
}
