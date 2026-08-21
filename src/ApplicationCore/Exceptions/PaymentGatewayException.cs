using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Wraps a failure reported by the PayPal gateway. Carries the provider HTTP status (so a caller
/// error can be surfaced as a 4xx and a provider/transport failure as a 5xx) and, when available,
/// PayPal's issue code for an operator-actionable message. The message is always caller-safe —
/// no SDK/framework internals leak through it.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int? statusCode = null, string? issue = null)
        : base(message)
    {
        StatusCode = statusCode;
        Issue = issue;
    }

    public PaymentGatewayException(string message, Exception inner, int? statusCode = null, string? issue = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        Issue = issue;
    }

    /// <summary>The HTTP status PayPal returned, when known.</summary>
    public int? StatusCode { get; }

    /// <summary>PayPal's issue code, when known.</summary>
    public string? Issue { get; }
}

/// <summary>
/// Thrown when PayPal answers a card payment with a challenge that would require the shopper to
/// approve it in a browser. The integration deliberately does not build an approval round-trip —
/// this surfaces the situation so an operator can act on it.
/// </summary>
public class PaymentApprovalRequiredException : PaymentGatewayException
{
    public PaymentApprovalRequiredException(string message)
        : base(message, statusCode: 409) { }
}

/// <summary>
/// Thrown when a capture fails because the authorization (hold) has gone stale. Signals the
/// fulfilment flow to renew (reauthorize) the hold rather than failing outright.
/// </summary>
public class AuthorizationExpiredException : PaymentGatewayException
{
    public AuthorizationExpiredException(string message, string? issue = null)
        : base(message, statusCode: 422, issue: issue) { }
}

/// <summary>
/// Thrown when a stale authorization can no longer be renewed. Carries a message an operator can
/// act on (the order must be re-placed / re-paid).
/// </summary>
public class AuthorizationNotRenewableException : PaymentGatewayException
{
    public AuthorizationNotRenewableException(string message, string? issue = null)
        : base(message, statusCode: 422, issue: issue) { }
}
