using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at the PayPal gateway boundary. Translates every SDK failure (typed API error, raw
/// error, transport failure, or an unprocessable body) into a single caller-safe type. Carries a
/// classified HTTP status (provider 4xx surfaces as a client 4xx; transport/unknown as 5xx) and,
/// when PayPal supplied one, an operator-actionable issue token in <see cref="PaymentApiException.ErrorCode"/>.
/// </summary>
public sealed class PayPalException : PaymentApiException
{
    public PayPalException(int statusCode, string message, string? issue = null)
        : base(statusCode, message, issue)
    {
    }

    public PayPalException(int statusCode, string message, string? issue, Exception inner)
        : base(statusCode, message, issue, inner)
    {
    }

    /// <summary>The PayPal issue token, when present (alias of <see cref="PaymentApiException.ErrorCode"/>).</summary>
    public string? Issue => ErrorCode;
}
