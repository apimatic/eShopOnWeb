using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class SmsProviderException : Exception
{
    public SmsProviderException(string message, HttpStatusCode statusCode, int? providerCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public HttpStatusCode StatusCode { get; }
    public int? ProviderCode { get; }
}
