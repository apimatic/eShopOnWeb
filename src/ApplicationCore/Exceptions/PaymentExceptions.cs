using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment operation violates domain rules or reaches an invalid state.
/// </summary>
public class PaymentDomainException : Exception
{
    public PaymentDomainException(string message) : base(message) { }

    public PaymentDomainException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Raised when a requested entity does not exist or does not belong to the caller.
/// </summary>
public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message) { }
}

/// <summary>
/// Raised when a requested entity exists but is in a state that forbids the operation.
/// </summary>
public class OperationConflictException : Exception
{
    public OperationConflictException(string message) : base(message) { }
}

/// <summary>
/// Raised when PayPal rejected or failed a call. Carries no payment credentials.
/// </summary>
public class PayPalApiException : Exception
{
    public int StatusCode { get; }
    public string? PayPalIssue { get; }
    public string? DebugId { get; }

    public PayPalApiException(string message, int statusCode = 0, string? payPalIssue = null, string? debugId = null)
        : base(message)
    {
        StatusCode = statusCode;
        PayPalIssue = payPalIssue;
        DebugId = debugId;
    }
}
