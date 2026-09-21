using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The card payment cannot complete without a shopper approving a challenge (e.g. 3-D Secure) in a
/// browser. This integration deliberately does not build an approval round-trip — the condition is
/// surfaced so an operator can act on it rather than being silently retried.
/// </summary>
public class PaymentChallengeRequiredException : Exception
{
    public PaymentChallengeRequiredException(string message) : base(message)
    {
    }
}
