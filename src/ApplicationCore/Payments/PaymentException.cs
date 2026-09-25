using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A payment operation failed in a way the caller should see, carrying the HTTP status to surface. Used for
/// both provider failures translated at the SDK boundary and application rule violations (ownership,
/// not-found, refund-cap). The message is always caller-safe — no card data, no raw SDK/provider dump.
/// </summary>
public class PaymentException : Exception
{
    public int StatusCode { get; }

    public PaymentException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public static PaymentException NotFound(string message) => new(404, message);
    public static PaymentException Conflict(string message) => new(409, message);
    public static PaymentException Validation(string message) => new(400, message);
    public static PaymentException ProviderUnavailable(string message, Exception? inner = null) => new(502, message, inner);
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires a shopper to approve in a browser. This
/// integration deliberately does not build an approval round-trip — the operation stops and reports it.
/// </summary>
public class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException(string message)
        : base(422, message) { }
}
