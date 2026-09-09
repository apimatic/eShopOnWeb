using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when a requested order does not exist or is not owned by the caller.</summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"No order found with id {orderId}") { }
}

/// <summary>Raised when a requested saved card does not exist or is not owned by the caller.</summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved card found with id {paymentMethodId}") { }
}

/// <summary>
/// Raised when an operation is invalid for the order's current state
/// (e.g. paying an order that is not awaiting payment, refunding before fulfilment,
/// or refunding more than was captured). Maps to HTTP 409 Conflict.
/// </summary>
public class InvalidPaymentOperationException : Exception
{
    public InvalidPaymentOperationException(string message) : base(message) { }
}

/// <summary>
/// Raised at fulfilment when the authorization is stale and can no longer be renewed
/// (PayPal's reauthorization window has closed). The message is phrased for an operator
/// to act on. Maps to HTTP 409 Conflict.
/// </summary>
public class AuthorizationCannotBeRenewedException : Exception
{
    public AuthorizationCannotBeRenewedException(string message) : base(message) { }
}

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that requires the shopper to
/// approve in a browser (3-D Secure / SCA). Per the integration's scope there is no browser
/// round-trip, so this surfaces the gap rather than building an approval flow.
/// Maps to HTTP 409 Conflict.
/// </summary>
public class PayPalChallengeRequiredException : Exception
{
    public PayPalChallengeRequiredException(string message) : base(message) { }
}

/// <summary>
/// Wraps a non-success response from the PayPal REST API, preserving the HTTP status and the
/// primary issue code so callers/operators can act on it. Maps to HTTP 502 Bad Gateway.
/// </summary>
public class PayPalApiException : Exception
{
    public int HttpStatusCode { get; }
    public string? IssueName { get; }
    public string? DebugId { get; }

    public PayPalApiException(int httpStatusCode, string? issueName, string? debugId, string message)
        : base(message)
    {
        HttpStatusCode = httpStatusCode;
        IssueName = issueName;
        DebugId = debugId;
    }
}
