using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the request is well formed but the plan cannot be subscribed to through this
/// integration - for example the plan requires a stored payment method and eShopOnWeb does not
/// capture one.
/// </summary>
public class SubscriptionNotAllowedException : Exception
{
    public SubscriptionNotAllowedException(string message) : base(message)
    {
    }
}
