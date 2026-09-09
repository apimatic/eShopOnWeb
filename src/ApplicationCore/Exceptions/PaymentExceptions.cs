using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for domain-level payment failures that should surface to the caller as an
/// actionable 4xx rather than a 500. Carries a status code hint for the API layer.
/// </summary>
public abstract class PaymentException : Exception
{
    protected PaymentException(string message) : base(message) { }
    protected PaymentException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The requested order does not exist, or does not belong to the caller.</summary>
public class OrderNotFoundException : PaymentException
{
    public OrderNotFoundException(int orderId)
        : base($"Order {orderId} was not found.") { }
}

/// <summary>The requested saved card does not exist, or does not belong to the caller.</summary>
public class PaymentMethodNotFoundException : PaymentException
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"Payment method {paymentMethodId} was not found.") { }
}

/// <summary>An operation was attempted against an order/payment that is in the wrong state for it.</summary>
public class InvalidPaymentStateException : PaymentException
{
    public InvalidPaymentStateException(string message) : base(message) { }
}

/// <summary>A refund would take the total refunded amount beyond what was captured.</summary>
public class RefundExceedsCaptureException : InvalidPaymentStateException
{
    public RefundExceedsCaptureException(decimal requested, decimal remaining)
        : base($"Refund of {requested:0.00} exceeds the remaining refundable amount of {remaining:0.00}.") { }
}

/// <summary>
/// PayPal returned a challenge that requires the shopper to approve the payment in a browser.
/// The task scope explicitly excludes building an approval round-trip, so this is surfaced as a gap.
/// </summary>
public class PaymentApprovalRequiredException : PaymentException
{
    public PaymentApprovalRequiredException(string message) : base(message) { }
}

/// <summary>
/// A hold could no longer be renewed (e.g. the authorization is too old to reauthorize), stated in
/// terms an operator can act on.
/// </summary>
public class AuthorizationNotRenewableException : PaymentException
{
    public AuthorizationNotRenewableException(string message) : base(message) { }
}

/// <summary>A call to the PayPal gateway failed. Carries PayPal's debug id when available.</summary>
public class PaymentGatewayException : PaymentException
{
    public string? DebugId { get; }

    public PaymentGatewayException(string message, string? debugId = null) : base(message)
    {
        DebugId = debugId;
    }

    public PaymentGatewayException(string message, Exception inner) : base(message, inner) { }
}
