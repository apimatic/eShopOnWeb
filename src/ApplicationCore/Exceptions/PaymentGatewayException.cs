using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure returned by the payment provider (PayPal) or the transport to it. Carries only
/// caller-safe fields — never a raw SDK/framework message or a request body.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, Exception? inner = null,
        int? statusCode = null, string? providerName = null, string? debugId = null,
        bool authorizationExpired = false)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ProviderName = providerName;
        DebugId = debugId;
        AuthorizationExpired = authorizationExpired;
    }

    /// <summary>HTTP status the provider returned, when one is available.</summary>
    public int? StatusCode { get; }

    /// <summary>PayPal error name/code (e.g. UNPROCESSABLE_ENTITY, RESOURCE_NOT_FOUND).</summary>
    public string? ProviderName { get; }

    /// <summary>PayPal correlation id (debug_id) for support/log correlation.</summary>
    public string? DebugId { get; }

    /// <summary>True when the failure indicates the authorization can no longer be captured as-is.</summary>
    public bool AuthorizationExpired { get; }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires shopper approval in a browser
/// (e.g. 3-D Secure). This integration deliberately does not build an approval round-trip.
/// </summary>
public class PayerActionRequiredException : PaymentGatewayException
{
    public PayerActionRequiredException(string message, string? debugId = null)
        : base(message, debugId: debugId)
    {
    }
}
