using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioProviderException : Exception
{
    public TwilioProviderException(string message, HttpStatusCode? statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
