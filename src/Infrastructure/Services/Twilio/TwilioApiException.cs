using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioApiException : Exception
{
    public TwilioApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
