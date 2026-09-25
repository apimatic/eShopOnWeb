using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the PayPal gateway raises. It carries only caller-safe facts — the HTTP status,
/// PayPal's fine-grained issue code(s) and the correlation debug id — never a raw SDK exception or body.
/// </summary>
public class PayPalGatewayException : Exception
{
    public PayPalGatewayException(string message, int? statusCode = null, string? issue = null,
        string? debugId = null, bool outcomeUnknown = false, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Issue = issue;
        DebugId = debugId;
        OutcomeUnknown = outcomeUnknown;
    }

    /// <summary>The HTTP status PayPal returned, when the provider answered.</summary>
    public int? StatusCode { get; }

    /// <summary>PayPal's first fine-grained issue code (e.g. AUTHORIZATION_EXPIRED, INSTRUMENT_DECLINED).</summary>
    public string? Issue { get; }

    /// <summary>PayPal's correlation id for support/log correlation.</summary>
    public string? DebugId { get; }

    /// <summary>True when a write may or may not have landed (transport failure that could not be settled).</summary>
    public bool OutcomeUnknown { get; }

    /// <summary>Issue codes that mean the authorization is stale and should be renewed before capture.</summary>
    public static readonly IReadOnlySet<string> StaleAuthorizationIssues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AUTHORIZATION_EXPIRED",
        "AUTH_EXPIRED"
    };

    public bool IsStaleAuthorization => Issue is not null && StaleAuthorizationIssues.Contains(Issue);
}
