using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The request was understood but cannot be applied in the order's current state.
/// </summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}

/// <summary>
/// The supplied payment data is missing or invalid.
/// </summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}

/// <summary>
/// The requested order, payment or saved card was not found for the caller.
/// </summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}

/// <summary>
/// Finix returned an error, or could not be reached.
/// </summary>
public class FinixGatewayException : Exception
{
    public FinixGatewayException(string message, string? errorCode = null,
        int? upstreamStatusCode = null, string? relatedTransferId = null) : base(message)
    {
        ErrorCode = errorCode;
        UpstreamStatusCode = upstreamStatusCode;
        RelatedTransferId = relatedTransferId;
    }

    public string? ErrorCode { get; }
    public int? UpstreamStatusCode { get; }
    public string? RelatedTransferId { get; }
}
