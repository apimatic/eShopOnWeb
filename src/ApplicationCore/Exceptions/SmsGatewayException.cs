using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
