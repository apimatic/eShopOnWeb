using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The payment processor (PayPal) rejected an operation. The message is written to be
/// actionable for an operator. <see cref="Retryable"/> hints whether retrying may help.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, bool retryable = false) : base(message)
    {
        Retryable = retryable;
    }

    public PaymentGatewayException(string message, Exception innerException, bool retryable = false)
        : base(message, innerException)
    {
        Retryable = retryable;
    }

    public bool Retryable { get; }
}

/// <summary>
/// A capture failed because the authorization is stale/expired/voided and can no longer be
/// captured as-is. Signals the fulfilment flow to attempt a re-authorization before giving up.
/// </summary>
public class AuthorizationExpiredException : PaymentGatewayException
{
    public AuthorizationExpiredException(string message, Exception? innerException = null)
        : base(message, innerException ?? new Exception(message), retryable: true) { }
}

/// <summary>
/// A stale authorization could not be renewed (e.g. beyond PayPal's re-authorization window),
/// so the money can no longer be captured. The operator must place a fresh order/payment.
/// </summary>
public class AuthorizationNotRenewableException : PaymentGatewayException
{
    public AuthorizationNotRenewableException(string message) : base(message, retryable: false) { }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a
/// browser (e.g. 3-D Secure). This integration is browser-less by mandate, so the operation is
/// stopped rather than building an approval round-trip.
/// </summary>
public class PaymentChallengeRequiredException : PaymentGatewayException
{
    public PaymentChallengeRequiredException(string message) : base(message, retryable: false) { }
}
