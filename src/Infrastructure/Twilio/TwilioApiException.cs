using System;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioApiException : Exception
{
    public TwilioApiException(int statusCode, int? code, string message)
        : base(RedactPhoneNumbers(message))
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public int? Code { get; }

    public static string RedactPhoneNumbers(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value, @"\+?\d[\d\s\-().]{6,}\d", "[redacted]");
    }
}
