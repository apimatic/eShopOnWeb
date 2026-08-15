using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raised when a PayPal call fails. Carries a caller-safe message plus, where available, PayPal's
/// HTTP status and debug id so an operator can act on it. <see cref="IsClientError"/> distinguishes
/// a caller-fixable rejection (a bad card, a conflict) from a processor/transport failure.
/// </summary>
public class PayPalGatewayException : Exception
{
    public int? HttpStatusCode { get; }
    public string? DebugId { get; }

    /// <summary>True when PayPal rejected the request in a way the caller can act on (a 4xx).</summary>
    public bool IsClientError { get; }

    public PayPalGatewayException(string message, int? httpStatusCode = null, string? debugId = null,
        bool isClientError = false, Exception? inner = null)
        : base(message, inner)
    {
        HttpStatusCode = httpStatusCode;
        DebugId = debugId;
        IsClientError = isClientError;
    }
}

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that would require a shopper to
/// approve in a browser. This integration does not build an approval round-trip; it surfaces the
/// challenge so it can be reported.
/// </summary>
public class PayPalChallengeRequiredException : PayPalGatewayException
{
    public PayPalChallengeRequiredException(string message, int? httpStatusCode = null, string? debugId = null)
        : base(message, httpStatusCode, debugId, isClientError: true)
    {
    }
}

/// <summary>
/// Raised when a stale authorization can no longer be renewed (reauthorized) and therefore the
/// order cannot be fulfilled — stated in terms an operator can act on.
/// </summary>
public class AuthorizationNoLongerHonorableException : PayPalGatewayException
{
    public AuthorizationNoLongerHonorableException(string message, int? httpStatusCode = null, string? debugId = null)
        : base(message, httpStatusCode, debugId, isClientError: true)
    {
    }
}
