using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioApiException : Exception
{
    public TwilioApiException(HttpStatusCode statusCode, int? code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public HttpStatusCode StatusCode { get; }
    public int? Code { get; }
}
