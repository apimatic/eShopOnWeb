using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// A single failure type the SMS provider abstraction raises, so callers handle one type instead of the
/// SDK's mixed exception surface. Carries the provider HTTP status where one is known, and never carries
/// a phone number or message body in its message.
/// </summary>
public sealed class SmsProviderException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// True when the request may have been received by the provider despite the failure (transport failure
    /// after the bytes went out), so the outcome is unknown rather than a definite failure.
    /// </summary>
    public bool OutcomeUnknown { get; }

    public SmsProviderException(string message, HttpStatusCode? statusCode = null, bool outcomeUnknown = false,
        Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        OutcomeUnknown = outcomeUnknown;
    }
}
