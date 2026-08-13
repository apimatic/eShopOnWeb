using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// The single failure type the SMS provider boundary raises, so callers have one type to handle
/// instead of the several the underlying SDK can throw (API error, transport failure, unreadable
/// response). Its message is always caller-safe and never contains a contact number.
/// </summary>
public class SmsProviderException : Exception
{
    public SmsProviderException(string message, int? statusCode = null, bool isDeterministicRejection = false, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        IsDeterministicRejection = isDeterministicRejection;
    }

    /// <summary>The provider HTTP status, when one was returned.</summary>
    public int? StatusCode { get; }

    /// <summary>
    /// True when the provider deterministically rejected the request (a 4xx) — retrying the same
    /// request cannot succeed. False for transport failures / unknown errors, which may be transient.
    /// </summary>
    public bool IsDeterministicRejection { get; }
}
