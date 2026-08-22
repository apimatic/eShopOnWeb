using System;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

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
}
