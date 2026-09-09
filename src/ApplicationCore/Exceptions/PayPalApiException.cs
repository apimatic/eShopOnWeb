using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a PayPal API call returns an error. Carries PayPal's own error identifier
/// (the <c>name</c>/<c>issue</c> from the spec's error model) so callers can react to specific
/// conditions — for example an expired authorization at fulfilment time.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string? payPalName, string message, string? debugId = null)
        : base(message)
    {
        StatusCode = statusCode;
        PayPalName = payPalName;
        DebugId = debugId;
    }

    /// <summary>HTTP status code PayPal returned.</summary>
    public int StatusCode { get; }

    /// <summary>PayPal's error name or the first issue code, e.g. <c>UNPROCESSABLE_ENTITY</c> / <c>AUTHORIZATION_EXPIRED</c>.</summary>
    public string? PayPalName { get; }

    /// <summary>PayPal debug id, useful when raising a support case.</summary>
    public string? DebugId { get; }
}
