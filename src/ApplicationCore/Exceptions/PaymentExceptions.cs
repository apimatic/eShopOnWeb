using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Base type for payment/fulfilment domain errors.</summary>
public abstract class PaymentException : Exception
{
    protected PaymentException(string message) : base(message) { }
    protected PaymentException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The request was malformed or missing required data. Maps to 400.</summary>
public class InvalidPaymentRequestException : PaymentException
{
    public InvalidPaymentRequestException(string message) : base(message) { }
}

/// <summary>Requested order does not exist. Maps to 404.</summary>
public class OrderNotFoundException : PaymentException
{
    public OrderNotFoundException(int orderId) : base($"No order found with id {orderId}.") { }
}

/// <summary>Requested saved card does not exist. Maps to 404.</summary>
public class PaymentMethodNotFoundException : PaymentException
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved payment method found with id {paymentMethodId}.") { }
}

/// <summary>The caller tried to see or act on data that is not theirs. Maps to 403.</summary>
public class ResourceForbiddenException : PaymentException
{
    public ResourceForbiddenException(string message) : base(message) { }
}

/// <summary>
/// The requested payment operation is not valid for the payment's current state
/// (e.g. refunding beyond the captured amount, capturing an uncaptured order). Maps to 409.
/// </summary>
public class PaymentStateException : PaymentException
{
    public PaymentStateException(string message) : base(message) { }
}

/// <summary>
/// A stale authorization could not be renewed, so fulfilment cannot proceed. Carries an
/// operator-actionable message. Maps to 409.
/// </summary>
public class AuthorizationRenewalException : PaymentException
{
    public AuthorizationRenewalException(string message) : base(message) { }
    public AuthorizationRenewalException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>PayPal returned an error we could not turn into a business outcome. Maps to 502.</summary>
public class PayPalApiException : PaymentException
{
    public PayPalApiException(string message) : base(message) { }
    public PayPalApiException(string message, Exception inner) : base(message, inner) { }

    /// <summary>PayPal error name (issue) when it could be parsed, e.g. AUTHORIZATION_EXPIRED.</summary>
    public string? PayPalIssue { get; init; }
}
