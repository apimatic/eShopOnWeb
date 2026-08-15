using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Base type for payment-domain failures the API surfaces to callers/operators.</summary>
public abstract class PaymentException : Exception
{
    protected PaymentException(string message) : base(message) { }
    protected PaymentException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The requested order/payment/saved-card does not exist (or isn't the caller's). Maps to 404.</summary>
public class PaymentNotFoundException : PaymentException
{
    public PaymentNotFoundException(string message) : base(message) { }
}

/// <summary>The request was malformed (empty order, unknown catalog item, missing funding source). Maps to 400.</summary>
public class PaymentValidationException : PaymentException
{
    public PaymentValidationException(string message) : base(message) { }
}

/// <summary>The operation is not valid for the payment's current state (e.g. capture before authorize,
/// refund beyond the captured amount). Maps to 409.</summary>
public class PaymentStateException : PaymentException
{
    public PaymentStateException(string message) : base(message) { }
}

/// <summary>PayPal rejected the payment operation. <see cref="PayPalStatusCode"/> and
/// <see cref="PayPalDetail"/> carry what PayPal reported, verbatim, for operator/shopper action.</summary>
public class PayPalApiException : PaymentException
{
    public int? PayPalStatusCode { get; }
    public string? PayPalDetail { get; }

    public PayPalApiException(string message, int? payPalStatusCode = null, string? payPalDetail = null, Exception? inner = null)
        : base(message, inner ?? new Exception(message))
    {
        PayPalStatusCode = payPalStatusCode;
        PayPalDetail = payPalDetail;
    }
}

/// <summary>The authorization can no longer be captured (typically because it has gone stale). The
/// caller should attempt to reauthorize before failing the fulfilment.</summary>
public class AuthorizationNotCapturableException : PaymentException
{
    public AuthorizationNotCapturableException(string message, Exception? inner = null)
        : base(message, inner ?? new Exception(message)) { }
}

/// <summary>A stale authorization can no longer be renewed. The message is operator-actionable
/// (surface it verbatim) — e.g. the shopper must place and pay for the order again.</summary>
public class AuthorizationNotReauthorizableException : PaymentException
{
    public string? PayPalDetail { get; }

    public AuthorizationNotReauthorizableException(string message, string? payPalDetail = null, Exception? inner = null)
        : base(message, inner ?? new Exception(message))
    {
        PayPalDetail = payPalDetail;
    }
}
