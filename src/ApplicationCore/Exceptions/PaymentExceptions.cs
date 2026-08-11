using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The requested order does not exist, or does not belong to the caller (existence is not disclosed).</summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"No order found with id {orderId}")
    {
    }
}

/// <summary>The requested saved payment method does not exist, or does not belong to the caller.</summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved payment method found with id {paymentMethodId}")
    {
    }
}

/// <summary>The request was well-formed but invalid for business reasons (e.g. no card and no saved method supplied).</summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message)
    {
    }
}

/// <summary>
/// The operation is not allowed for the order's current payment state
/// (e.g. capturing before authorization, cancelling after capture, refunding before capture).
/// Mapped to HTTP 409 Conflict.
/// </summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message)
    {
    }
}

/// <summary>
/// A call to PayPal failed. Carries PayPal's own issue name and description so an operator can act on it.
/// </summary>
public class PayPalApiException : Exception
{
    public int HttpStatusCode { get; }
    public string? Issue { get; }
    public string? Debug { get; }

    public PayPalApiException(int httpStatusCode, string? issue, string message, string? debug = null)
        : base(message)
    {
        HttpStatusCode = httpStatusCode;
        Issue = issue;
        Debug = debug;
    }
}

/// <summary>
/// PayPal answered a card payment with a 3-D Secure / challenge that requires the shopper to approve in a browser.
/// This integration deliberately does not build an approval round-trip; the condition is surfaced to the caller.
/// </summary>
public class PayPalChallengeRequiredException : Exception
{
    public PayPalChallengeRequiredException()
        : base("The card issuer requires the shopper to approve this payment in a browser (3-D Secure). " +
               "This integration does not support a browser approval round-trip.")
    {
    }
}
