using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioApiException : Exception
{
    public TwilioApiException(int statusCode, int? errorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }
    public int? ErrorCode { get; }

    public bool IsRetryable =>
        StatusCode == (int)HttpStatusCode.TooManyRequests
        || StatusCode == (int)HttpStatusCode.ServiceUnavailable
        || StatusCode == (int)HttpStatusCode.InternalServerError;
}
