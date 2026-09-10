using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a call to PayPal fails. Carries PayPal's HTTP status, its error <see cref="Name"/> and
/// <see cref="DebugId"/> so the failure can be surfaced to an operator and traced with PayPal support.
/// </summary>
public class PayPalGatewayException : Exception
{
    public int StatusCode { get; }
    public string? Name { get; }
    public string? DebugId { get; }

    public PayPalGatewayException(string message, int statusCode, string? name, string? debugId, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        Name = name;
        DebugId = debugId;
    }
}

/// <summary>
/// Thrown when PayPal answers a card payment with a challenge that requires the shopper to approve it in a
/// browser (for example a 3-D Secure step). This integration is browser-free by design, so the caller is told
/// the payment cannot be completed here rather than being taken through an approval round-trip.
/// </summary>
public class PayPalChallengeRequiredException : Exception
{
    public PayPalChallengeRequiredException(string message) : base(message)
    {
    }
}
