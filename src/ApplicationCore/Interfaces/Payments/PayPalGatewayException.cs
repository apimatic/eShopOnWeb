using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Raised when PayPal rejects a request. Carries the PayPal error name/details so callers can
/// surface something an operator or shopper can act on, rather than an opaque failure.
/// </summary>
public class PayPalGatewayException : Exception
{
    public PayPalGatewayException(string message, int statusCode = 0, string? payPalErrorName = null,
        string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        PayPalErrorName = payPalErrorName;
        DebugId = debugId;
    }

    /// <summary>The HTTP status PayPal returned, if any.</summary>
    public int StatusCode { get; }

    /// <summary>PayPal's machine-readable error name (e.g. <c>UNPROCESSABLE_ENTITY</c>), if any.</summary>
    public string? PayPalErrorName { get; }

    /// <summary>PayPal's debug id for support correlation, if any.</summary>
    public string? DebugId { get; }
}

/// <summary>
/// Raised at fulfilment when a stale authorization cannot be captured and can no longer be renewed.
/// The message is phrased for an operator: the hold is gone and the shopper must pay again.
/// </summary>
public class AuthorizationNotRenewableException : PayPalGatewayException
{
    public AuthorizationNotRenewableException(string message, Exception? inner = null)
        : base(message, inner: inner)
    {
    }
}
