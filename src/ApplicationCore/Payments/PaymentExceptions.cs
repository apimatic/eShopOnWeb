using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A failure reported by (or reaching us from) the payment processor. Carries an optional HTTP status and
/// PayPal's own <c>debug_id</c>/error name so the boundary can present a coherent, distinct, leak-free
/// error without surfacing SDK internals.
/// </summary>
public class PaymentGatewayException : Exception
{
    public int? StatusCode { get; }
    public string? DebugId { get; }
    public string? ProviderErrorName { get; }

    public PaymentGatewayException(string message, int? statusCode = null, string? debugId = null,
        string? providerErrorName = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        ProviderErrorName = providerErrorName;
    }
}

/// <summary>
/// The processor answered a card payment with a challenge that requires a shopper to approve in a browser
/// (e.g. 3-D Secure / payer action). This integration deliberately does not build an approval round-trip —
/// it stops and reports, per the task's constraint.
/// </summary>
public class PaymentChallengeRequiredException : PaymentGatewayException
{
    public PaymentChallengeRequiredException(string message)
        : base(message) { }
}

/// <summary>
/// A stale authorization could not be renewed (reauthorization was rejected). The message is written for an
/// operator to act on — e.g. place and authorize the order afresh.
/// </summary>
public class AuthorizationNotRenewableException : PaymentGatewayException
{
    public AuthorizationNotRenewableException(string message, Exception? inner = null)
        : base(message, inner: inner) { }
}

/// <summary>The requested order/payment/card was not found, or is not the caller's. Maps to 404.</summary>
public class PaymentEntityNotFoundException : Exception
{
    public PaymentEntityNotFoundException(string message) : base(message) { }
}

/// <summary>The operation is not valid for the payment's current state (e.g. cancel after fulfilment). Maps to 409.</summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}

/// <summary>The request was malformed (bad amount, missing/duplicate payment source, etc.). Maps to 400.</summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}
