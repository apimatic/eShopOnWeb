using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The requested order / payment / saved card does not exist. Maps to HTTP 404.</summary>
public class PaymentEntityNotFoundException : Exception
{
    public PaymentEntityNotFoundException(string message) : base(message) { }
}

/// <summary>The caller tried to see or act on data that is not theirs. Maps to HTTP 403.</summary>
public class ForbiddenPaymentException : Exception
{
    public ForbiddenPaymentException(string message) : base(message) { }
}

/// <summary>A payment operation is invalid for the current state (e.g. refund exceeds capture). Maps to HTTP 422.</summary>
public class InvalidPaymentOperationException : Exception
{
    public InvalidPaymentOperationException(string message) : base(message) { }
}

/// <summary>
/// A stale authorization could not be renewed before fulfilment. Carries an operator-actionable
/// message. Maps to HTTP 422.
/// </summary>
public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message) : base(message) { }
}

/// <summary>An error returned by the PayPal API. Maps to HTTP 502 (bad upstream gateway).</summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(string message, string? debugId = null, string? name = null) : base(message)
    {
        DebugId = debugId;
        Name = name;
    }

    /// <summary>PayPal's correlation/debug id, useful when raising a support case.</summary>
    public string? DebugId { get; }

    /// <summary>PayPal's machine-readable error name, when available.</summary>
    public string? Name { get; }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a
/// browser. This integration deliberately does not build an approval round-trip. Maps to HTTP 422.
/// </summary>
public class PayPalChallengeRequiredException : Exception
{
    public PayPalChallengeRequiredException(string message) : base(message) { }
}
