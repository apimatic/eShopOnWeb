using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the SMS abstraction raises. Carries the provider's HTTP status (when there was one)
/// so a boundary can map an <em>our-fault</em> status (401/403/429) or a caller-fault 4xx deliberately, and
/// flags an indeterminate outcome (a write that may have reached the provider once).
/// </summary>
public class SmsProviderException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    /// <summary>True when the send may have taken effect once but its outcome could not be confirmed.</summary>
    public bool OutcomeUnknown { get; }

    public SmsProviderException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null, bool outcomeUnknown = false)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        OutcomeUnknown = outcomeUnknown;
    }
}
