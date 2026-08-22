using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SmsProviderException : Exception
{
    public SmsProviderException(string message, HttpStatusCode? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    public HttpStatusCode? ProviderStatusCode { get; }
}
