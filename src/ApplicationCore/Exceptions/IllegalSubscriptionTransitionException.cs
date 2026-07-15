using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A requested action is not legal given the subscription's current state (or would be a
/// no-op) — rejected locally before any call reaches the billing provider. Distinct from
/// <see cref="BillingProviderException"/>, which means the provider itself was reached and
/// rejected (or was unreachable for) the request.
/// </summary>
public class IllegalSubscriptionTransitionException : Exception
{
    public IllegalSubscriptionTransitionException(string message) : base(message)
    {
    }
}
