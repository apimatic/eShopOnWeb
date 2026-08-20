using System;
using Microsoft.eShopWeb.ApplicationCore.Extensions;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioApiException : Exception
{
    public int StatusCode { get; }

    public TwilioApiException(int statusCode, string message)
        : base(PhoneNumberRedactor.Redact(message))
    {
        StatusCode = statusCode;
    }
}
