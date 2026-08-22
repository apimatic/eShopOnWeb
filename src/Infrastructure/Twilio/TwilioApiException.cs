using System;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal static class PhoneNumberRedactor
{
    private static readonly Regex PhonePattern = new(@"\+?\d[\d\s().\-]{6,}\d", RegexOptions.Compiled);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return PhonePattern.Replace(value, "[redacted]");
    }
}

public class TwilioApiException : Exception
{
    public TwilioApiException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
