using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a PayPal REST call returns an error. Carries the details an operator needs to act on the
/// failure (PayPal's <c>debug_id</c> and issue name) without leaking any card data.
/// </summary>
public class PayPalApiException : Exception
{
    public int StatusCode { get; }
    public string? DebugId { get; }
    public string? IssueName { get; }

    public PayPalApiException(string message, int statusCode, string? debugId, string? issueName)
        : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        IssueName = issueName;
    }

    private static readonly HashSet<string> ExpiredAuthorizationIssues = new(StringComparer.OrdinalIgnoreCase)
    {
        "AUTHORIZATION_EXPIRED",
        "AUTH_CAPTURE_CURRENCY_MISMATCH_INVALID",
        "PAYMENT_ALREADY_DONE",
        "AUTHORIZATION_VOIDED",
        "INVALID_RESOURCE_ID"
    };

    /// <summary>
    /// Whether the error indicates the authorization can no longer be captured because it has expired or
    /// been voided — the signal to attempt a renewal (reauthorize) before failing fulfilment.
    /// </summary>
    public bool IndicatesExpiredOrVoidedAuthorization =>
        IssueName is not null && ExpiredAuthorizationIssues.Contains(IssueName);
}
