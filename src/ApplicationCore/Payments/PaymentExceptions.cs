using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Base type for payment/domain failures that map to specific HTTP responses.</summary>
public abstract class PaymentException : Exception
{
    protected PaymentException(string message) : base(message) { }
    protected PaymentException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The requested resource does not exist, or does not belong to the caller (=> 404).</summary>
public class PaymentResourceNotFoundException : PaymentException
{
    public PaymentResourceNotFoundException(string message) : base(message) { }
}

/// <summary>The resource is in a state that does not allow the requested operation (=> 409).</summary>
public class PaymentStateConflictException : PaymentException
{
    public PaymentStateConflictException(string message) : base(message) { }
}

/// <summary>The request is well-formed but semantically invalid, e.g. refunding beyond capture (=> 422).</summary>
public class PaymentValidationException : PaymentException
{
    public PaymentValidationException(string message) : base(message) { }
}

/// <summary>
/// PayPal returned an error. Carries the HTTP status and PayPal <c>debug_id</c> so operators can trace it (=> 502).
/// </summary>
public class PayPalApiException : PaymentException
{
    public int StatusCode { get; }
    public string? DebugId { get; }
    public string? PayPalName { get; }

    public PayPalApiException(string message, int statusCode, string? debugId, string? payPalName)
        : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        PayPalName = payPalName;
    }
}

/// <summary>
/// PayPal answered a card payment with a contingency that requires the shopper to approve in a browser.
/// The task mandates we STOP rather than build an approval round-trip.
/// </summary>
public class PayPalChallengeRequiredException : PaymentException
{
    public PayPalChallengeRequiredException(string message) : base(message) { }
}
