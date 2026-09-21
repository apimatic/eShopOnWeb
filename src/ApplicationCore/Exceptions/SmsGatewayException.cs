using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the SMS provider integration surfaces at its boundary. It carries a
/// caller-safe message only — never the raw provider exception text, the destination number, or the
/// message body. <see cref="OutcomeUnknown"/> distinguishes a transport failure (the request may still
/// have been received) from a definite provider rejection.
/// </summary>
public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message, bool outcomeUnknown = false, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        OutcomeUnknown = outcomeUnknown;
        StatusCode = statusCode;
    }

    /// <summary>True when the send failed in transport and may nonetheless have reached the provider.</summary>
    public bool OutcomeUnknown { get; }

    /// <summary>The provider HTTP status where one was available, else null.</summary>
    public int? StatusCode { get; }
}
