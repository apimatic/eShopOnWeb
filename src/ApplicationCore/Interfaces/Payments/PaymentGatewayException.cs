using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// The single failure type the gateway boundary raises. Carries enough to present a coherent,
/// distinct, leak-free result to the caller: the transport <see cref="StatusCode"/> (when the provider
/// answered), whether the fault is the caller's to fix (<see cref="CallerFault"/>), and PayPal's own
/// correlation id (<see cref="DebugId"/>) for cross-referencing in logs.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int? statusCode = null, bool callerFault = false,
        string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        CallerFault = callerFault;
        DebugId = debugId;
    }

    /// <summary>HTTP status PayPal returned, when it answered at all.</summary>
    public int? StatusCode { get; }

    /// <summary>True when the caller sent something the provider rejected (a 4xx they can act on).</summary>
    public bool CallerFault { get; }

    /// <summary>PayPal's <c>debug_id</c> / correlation id, when present in the error body.</summary>
    public string? DebugId { get; }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a browser.
/// The integration deliberately does not build an approval round-trip; this surfaces the situation to
/// the operator instead.
/// </summary>
public sealed class PaymentApprovalRequiredException : PaymentGatewayException
{
    public PaymentApprovalRequiredException(string message)
        : base(message, statusCode: null, callerFault: false) { }
}

/// <summary>
/// A stale hold could not be renewed (e.g. it is beyond PayPal's reauthorization window), so the order
/// cannot be fulfilled against it. Phrased for an operator to act on.
/// </summary>
public sealed class AuthorizationNotRenewableException : PaymentGatewayException
{
    public AuthorizationNotRenewableException(string message, int? statusCode = null, string? debugId = null, Exception? inner = null)
        : base(message, statusCode, callerFault: false, debugId, inner) { }
}
