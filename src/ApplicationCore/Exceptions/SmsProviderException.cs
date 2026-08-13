using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a call to the messaging provider fails. Its message is deliberately free of PII —
/// it carries the HTTP status and the provider's own error code only, never the recipient number
/// or the raw provider response body (which can echo the number back).
/// </summary>
public class SmsProviderException : Exception
{
    public SmsProviderException(string message, int? httpStatusCode = null, int? errorCode = null)
        : base(message)
    {
        HttpStatusCode = httpStatusCode;
        ErrorCode = errorCode;
    }

    /// <summary>The HTTP status the provider returned, if the call reached it.</summary>
    public int? HttpStatusCode { get; }

    /// <summary>The provider's own error code, if the response carried one.</summary>
    public int? ErrorCode { get; }
}
