using System;
using System.Net;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class TwilioApiException : Exception
{
    private static readonly Regex PhoneLike = new(@"\+?\d[\d\s().-]{7,}\d", RegexOptions.Compiled);

    public TwilioApiException(string message, HttpStatusCode? statusCode = null, int? providerCode = null)
        : base(Sanitize(message))
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public HttpStatusCode? StatusCode { get; }

    public int? ProviderCode { get; }

    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return PhoneLike.Replace(text, "[redacted]");
    }
}
