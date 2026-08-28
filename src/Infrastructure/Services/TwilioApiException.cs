using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class TwilioApiException : Exception
{
    public TwilioApiException(HttpStatusCode statusCode, int? errorCode)
        : base("The messaging provider rejected the request.")
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public HttpStatusCode StatusCode { get; }
    public int? ErrorCode { get; }
}
