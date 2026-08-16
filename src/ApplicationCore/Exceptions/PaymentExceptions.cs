using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>A failure returned by the PayPal API, surfaced with enough detail to diagnose it.</summary>
public class PaymentGatewayException : Exception
{
    public int? StatusCode { get; }
    public string? PayPalErrorName { get; }
    public string? DebugId { get; }

    public PaymentGatewayException(string message, int? statusCode = null, string? payPalErrorName = null,
        string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        PayPalErrorName = payPalErrorName;
        DebugId = debugId;
    }
}

/// <summary>
/// A stale authorization could not be renewed, so the order cannot be captured as-is. Phrased for an operator:
/// a fresh authorization (the shopper paying again) is required.
/// </summary>
public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// PayPal answered a card payment with a challenge that needs the shopper to approve it in a browser.
/// This integration is server-to-server only; such a response is reported rather than handled.
/// </summary>
public class PaymentApprovalRequiredException : Exception
{
    public PaymentApprovalRequiredException(string message) : base(message) { }
}

/// <summary>A payment action was attempted against an order in a state that does not permit it.</summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message) { }
}

/// <summary>An order could not be placed because the request was invalid (empty, bad quantity, unknown item).</summary>
public class OrderPlacementException : Exception
{
    public OrderPlacementException(string message) : base(message) { }
}

/// <summary>The requested order does not exist, or does not belong to the caller.</summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"Order {orderId} was not found.") { }
}
