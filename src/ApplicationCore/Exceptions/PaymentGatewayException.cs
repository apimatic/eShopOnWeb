using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised by the payment gateway when the processor rejects a request, is unreachable, or answers with a
/// body that cannot be processed. Carries enough to map the failure back to a meaningful caller status and
/// to give an operator something to act on: the HTTP status (when known), the provider's own error name and
/// per-issue codes, and PayPal's <c>debug_id</c> correlation id.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(
        string message,
        int? statusCode = null,
        string? providerErrorName = null,
        IReadOnlyList<string>? issues = null,
        string? debugId = null,
        Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ProviderErrorName = providerErrorName;
        Issues = issues ?? Array.Empty<string>();
        DebugId = debugId;
    }

    /// <summary>The HTTP status PayPal returned, when the failure carried one.</summary>
    public int? StatusCode { get; }

    /// <summary>PayPal's human-readable error name (e.g. UNPROCESSABLE_ENTITY).</summary>
    public string? ProviderErrorName { get; }

    /// <summary>The per-issue codes PayPal reported (e.g. AUTHORIZATION_EXPIRED).</summary>
    public IReadOnlyList<string> Issues { get; }

    /// <summary>PayPal's correlation id for support/reconciliation.</summary>
    public string? DebugId { get; }

    /// <summary>True when the failure indicates the authorization has expired and could be re-authorized.</summary>
    public bool IsAuthorizationExpired => HasIssue("AUTHORIZATION_EXPIRED");

    /// <summary>True when PayPal indicates the authorization can no longer be captured or re-authorized.</summary>
    public bool IsAuthorizationCannotBeReauthorized =>
        HasIssue("MAX_NUMBER_OF_REAUTHORIZATION_EXCEEDED") ||
        HasIssue("REAUTHORIZATION_TOO_SOON") ||
        HasIssue("AUTHORIZATION_VOIDED") ||
        HasIssue("INVALID_RESOURCE_ID");

    /// <summary>
    /// True when PayPal asked for a shopper approval / additional action (e.g. a 3DS browser challenge).
    /// Per the task this is a STOP-and-report condition, not something to build an approval round-trip for.
    /// </summary>
    public bool RequiresShopperApproval => HasIssue("PAYER_ACTION_REQUIRED");

    public bool HasIssue(string issue) =>
        Issues.Any(i => string.Equals(i, issue, StringComparison.OrdinalIgnoreCase));

    /// <summary>A compact, operator-facing description of the failure.</summary>
    public string ToOperatorMessage()
    {
        var parts = new List<string> { Message };
        if (Issues.Count > 0) parts.Add($"issues: {string.Join(", ", Issues)}");
        if (!string.IsNullOrEmpty(ProviderErrorName)) parts.Add($"error: {ProviderErrorName}");
        if (!string.IsNullOrEmpty(DebugId)) parts.Add($"debug_id: {DebugId}");
        return string.Join(" | ", parts);
    }
}
