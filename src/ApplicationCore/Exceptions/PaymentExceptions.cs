using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure returned by (or while talking to) the PayPal payment processor. <see cref="ProviderStatusCode"/>
/// carries the provider's HTTP status when known so the API boundary can map a provider 4xx back to a client
/// 4xx and everything else to a 5xx. The message is always caller-safe (no SDK/JSON internals).
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int? providerStatusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        ProviderStatusCode = providerStatusCode;
    }

    public int? ProviderStatusCode { get; }
}

/// <summary>
/// The request the shopper made cannot proceed as asked (e.g. refunding more than was captured, or a
/// missing/duplicated payment instrument). Maps to a 400.
/// </summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}

/// <summary>
/// A stale authorization could no longer be renewed before fulfilment. The message is phrased for an
/// operator to act on and carries PayPal's own reason.
/// </summary>
public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message) : base(message) { }
}

/// <summary>
/// PayPal answered the card payment with a challenge that would require the shopper to approve in a browser.
/// Per the integration's scope we stop and report rather than building an approval round-trip.
/// </summary>
public class PaymentChallengeRequiredException : Exception
{
    public PaymentChallengeRequiredException(string message) : base(message) { }
}

/// <summary>
/// The requested order does not exist, or does not belong to the caller. Both cases surface identically
/// (a 404) so one shopper can never learn about another's orders. Maps to a 404.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"Order {orderId} was not found.") { }
}
