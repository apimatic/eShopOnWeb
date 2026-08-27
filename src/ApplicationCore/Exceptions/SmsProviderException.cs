using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the messaging-provider boundary. Carries the provider's HTTP status when one
/// exists; the message is always caller-safe (no credentials, no phone numbers, no SDK internals).
/// </summary>
public class SmsProviderException : Exception
{
    public SmsProviderException(string message, HttpStatusCode? statusCode, Exception? innerException = null,
        bool outcomeUnknown = false)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        OutcomeUnknown = outcomeUnknown;
    }

    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// True when a write may have reached the provider before the failure (e.g. the connection
    /// dropped after the request went out) — the outcome must be settled by re-reading provider
    /// state, not assumed to be a failure.
    /// </summary>
    public bool OutcomeUnknown { get; }
}
