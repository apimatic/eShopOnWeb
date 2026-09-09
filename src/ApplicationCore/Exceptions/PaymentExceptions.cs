using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Resource not found, or not owned by the caller (kept indistinguishable to avoid leaking existence).</summary>
public class NotFoundException : ApiException
{
    public NotFoundException(string message) : base(404, message) { }
}

/// <summary>The request is understood but not allowed in the order/payment's current state.</summary>
public class PaymentStateException : ApiException
{
    public PaymentStateException(string message) : base(409, message) { }
}

/// <summary>The request is malformed or violates a business rule (e.g. refund exceeds captured).</summary>
public class PaymentValidationException : ApiException
{
    public PaymentValidationException(string message) : base(422, message) { }
}

/// <summary>
/// PayPal returned an error for a card/payment call (decline, gateway error, or a request the
/// spec's error model describes). Carries PayPal's own name/debug id/issue codes so an operator
/// can act on it.
/// </summary>
public class PayPalGatewayException : ApiException
{
    public PayPalGatewayException(int statusCode, string name, string message, string? debugId,
        IReadOnlyList<string> issues, int upstreamStatusCode)
        : base(statusCode, message)
    {
        Name = name;
        DebugId = debugId;
        Issues = issues;
        UpstreamStatusCode = upstreamStatusCode;
    }

    public string Name { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }
    public int UpstreamStatusCode { get; }

    public bool HasIssue(params string[] issueNames) =>
        Issues.Any(i => issueNames.Any(n => string.Equals(i, n, StringComparison.OrdinalIgnoreCase)));
}

/// <summary>
/// PayPal answered the card payment with a challenge that requires a shopper to approve in a
/// browser. The task mandates stopping and reporting this rather than building an approval round-trip.
/// </summary>
public class PayPalChallengeRequiredException : ApiException
{
    public PayPalChallengeRequiredException(string message) : base(402, message) { }
}
