using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>How an SMS provider failure should be treated by callers.</summary>
public enum SmsGatewayErrorKind
{
    /// <summary>A temporary problem (provider 5xx, timeout, or the provider was unreachable). Safe to retry later.</summary>
    Transient,

    /// <summary>The provider rejected the request in a way retrying cannot fix (a permanent 4xx that is the caller's to correct).</summary>
    Rejected,

    /// <summary>The failure could not be classified.</summary>
    Unknown
}

/// <summary>
/// The single failure type the SMS gateway raises. It hides every SDK/transport exception behind one
/// domain type carrying the provider's HTTP status (when there was one) so callers can react coherently:
/// a transient failure is worth retrying, a rejection is not.
/// </summary>
public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message, SmsGatewayErrorKind kind, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
    }

    public SmsGatewayErrorKind Kind { get; }

    /// <summary>The provider's HTTP status code, when the provider answered at all.</summary>
    public int? StatusCode { get; }
}
