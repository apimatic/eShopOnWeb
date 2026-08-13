using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Thrown when a Twilio API call returns an error. Models Twilio's standard error body
/// (<c>code</c>, <c>message</c>, <c>more_info</c>, <c>status</c>).
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(HttpStatusCode httpStatusCode, int? code, string? message, string? moreInfo)
        : base(BuildMessage(httpStatusCode, code, message))
    {
        HttpStatusCode = httpStatusCode;
        Code = code;
        MoreInfo = moreInfo;
    }

    public HttpStatusCode HttpStatusCode { get; }

    /// <summary>Twilio's numeric error code (e.g. 21211), when present.</summary>
    public int? Code { get; }

    public string? MoreInfo { get; }

    private static string BuildMessage(HttpStatusCode httpStatusCode, int? code, string? message)
    {
        // Note: Twilio error messages can echo the phone number involved. Callers that persist or log
        // this must scrub it; the code and HTTP status are the number-free parts to rely on.
        var codePart = code.HasValue ? $" (code {code.Value})" : string.Empty;
        return $"Twilio API call failed with HTTP {(int)httpStatusCode}{codePart}: {message}";
    }
}
