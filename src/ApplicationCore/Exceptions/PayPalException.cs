using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the PayPal integration surfaces. It carries only caller-safe detail — never a raw
/// SDK/framework message. <see cref="StatusCode"/> is PayPal's HTTP status when there was one; a business-rule
/// rejection (e.g. "refund exceeds capture", "authorization no longer reauthorizable") sets
/// <see cref="IsBusinessRule"/> so the caller can translate it into an actionable message rather than an outage.
/// </summary>
public class PayPalException : Exception
{
    public PayPalException(string message, HttpStatusCode? statusCode = null, bool isBusinessRule = false,
        bool isTransient = false, string? issue = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        IsBusinessRule = isBusinessRule;
        IsTransient = isTransient;
        Issue = issue;
    }

    public HttpStatusCode? StatusCode { get; }

    /// <summary>A deterministic rejection PayPal will keep making — not worth retrying, and actionable.</summary>
    public bool IsBusinessRule { get; }

    /// <summary>A transport failure or 5xx — the outcome may be unknown and a retry could succeed.</summary>
    public bool IsTransient { get; }

    /// <summary>PayPal's machine-readable issue code, when it supplied one.</summary>
    public string? Issue { get; }
}
