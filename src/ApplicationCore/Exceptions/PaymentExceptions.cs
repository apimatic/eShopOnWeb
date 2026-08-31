using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation failed at the payment provider. The message is safe and
/// meaningful for an operator or shopper; technical detail (PayPal debug id)
/// is carried separately.
/// </summary>
public class PaymentProcessingException : Exception
{
    public PaymentProcessingException(string message, string? providerDebugId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderDebugId = providerDebugId;
    }

    public string? ProviderDebugId { get; }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to
/// approve in a browser (e.g. 3-D Secure). This integration is API-only and does
/// not implement that round-trip.
/// </summary>
public class PaymentActionRequiredException : PaymentProcessingException
{
    public PaymentActionRequiredException(string message, string? providerDebugId = null)
        : base(message, providerDebugId)
    {
    }
}

/// <summary>
/// An authorization has gone stale and can no longer be renewed, so the order
/// cannot be fulfilled against it. The shopper must pay again.
/// </summary>
public class AuthorizationNotRenewableException : PaymentProcessingException
{
    public AuthorizationNotRenewableException(string message, string? providerDebugId = null)
        : base(message, providerDebugId)
    {
    }
}
