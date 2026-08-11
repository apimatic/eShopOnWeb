using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// The single failure type the payment gateway surfaces. It carries a caller-safe message plus the
/// provider's HTTP status and issue codes, so callers can distinguish a client-side rejection (4xx) from
/// a provider/transport failure (5xx) and act on specific PayPal issues (e.g. an expired authorization).
/// Provider internals and raw exception text are never leaked through <see cref="Exception.Message"/>.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int? statusCode = null, IReadOnlyList<string>? issues = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        Issues = issues ?? Array.Empty<string>();
    }

    /// <summary>Provider HTTP status, when known. Null for transport failures.</summary>
    public int? StatusCode { get; }

    /// <summary>PayPal issue codes (e.g. AUTHORIZATION_EXPIRED, INSTRUMENT_DECLINED), when present.</summary>
    public IReadOnlyList<string> Issues { get; }

    /// <summary>A client-actionable failure (the caller sent something the provider rejected).</summary>
    public bool IsClientError => StatusCode is >= 400 and < 500;

    public bool HasIssue(string issueCode) =>
        Issues.Any(i => string.Equals(i, issueCode, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Thrown when PayPal answers a card payment with a challenge that requires the shopper to approve in a
/// browser (3-D Secure / payer-action-required). The integration deliberately does not build an approval
/// round-trip; this surfaces the situation so an operator can act on it.
/// </summary>
public class PaymentApprovalRequiredException : PaymentGatewayException
{
    public PaymentApprovalRequiredException(string message)
        : base(message, statusCode: 402)
    {
    }
}
