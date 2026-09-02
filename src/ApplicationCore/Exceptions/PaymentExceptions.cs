using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested order or saved payment method does not exist, or does not belong to the
/// caller. Existence of other shoppers' data is never leaked.
/// </summary>
public class PaymentResourceNotFoundException : Exception
{
    public PaymentResourceNotFoundException(string message) : base(message) { }
}

/// <summary>The order is in a state that does not allow the requested payment operation.</summary>
public class InvalidPaymentStateException : Exception
{
    public InvalidPaymentStateException(string message) : base(message) { }
}

/// <summary>A refund would exceed what was actually captured for the order.</summary>
public class RefundExceedsCapturedException : Exception
{
    public RefundExceedsCapturedException(string message) : base(message) { }
}

/// <summary>
/// PayPal requires the shopper to complete an authentication challenge (e.g. 3-D Secure)
/// in a browser before the payment can be authorized. This integration does not drive
/// browser approval round-trips.
/// </summary>
public class PayerActionRequiredException : Exception
{
    public PayerActionRequiredException(string message) : base(message) { }
}

/// <summary>
/// The authorization on an order went stale before fulfilment and PayPal can no longer
/// renew it. The operator must ask the shopper to pay again (or cancel the order).
/// </summary>
public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message) : base(message) { }
}

/// <summary>PayPal declined the payment authorization.</summary>
public class PaymentDeclinedException : Exception
{
    public PaymentDeclinedException(string message) : base(message) { }
}

/// <summary>
/// An error returned by the PayPal API. Carries PayPal's own error name, message and
/// debug id so operators can correlate with PayPal support. Never contains card data.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(int httpStatusCode, string? errorName, string message, string? debugId)
        : base($"PayPal error {errorName ?? "UNKNOWN"} (HTTP {httpStatusCode}): {message}")
    {
        HttpStatusCode = httpStatusCode;
        ErrorName = errorName;
        DebugId = debugId;
    }

    public int HttpStatusCode { get; }
    public string? ErrorName { get; }
    public string? DebugId { get; }
}
