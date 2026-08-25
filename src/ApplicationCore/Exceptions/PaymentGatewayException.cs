using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown by an <see cref="Interfaces.IPaymentGateway"/> implementation when PayPal rejects or fails an
/// operation. Carries enough of PayPal's own error payload for an operator to act on, without leaking
/// SDK/transport internals to callers.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, string? errorCode = null, IReadOnlyList<string>? details = null, bool isRetryable = false)
        : base(message)
    {
        ErrorCode = errorCode;
        Details = details ?? Array.Empty<string>();
        IsRetryable = isRetryable;
    }

    public PaymentGatewayException(string message, Exception innerException, string? errorCode = null, IReadOnlyList<string>? details = null, bool isRetryable = false)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        Details = details ?? Array.Empty<string>();
        IsRetryable = isRetryable;
    }

    /// <summary>PayPal's own short error code (e.g. "INSTRUMENT_DECLINED"), when available.</summary>
    public string? ErrorCode { get; }

    /// <summary>PayPal's own issue/description strings, when available.</summary>
    public IReadOnlyList<string> Details { get; }

    /// <summary>True for transient/transport failures where retrying the same logical operation is expected to be safe.</summary>
    public bool IsRetryable { get; }
}
