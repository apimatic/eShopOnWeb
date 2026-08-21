using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SmsProviderException : Exception
{
    public SmsProviderException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }

    public SmsProviderException(string message, HttpStatusCode statusCode, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
