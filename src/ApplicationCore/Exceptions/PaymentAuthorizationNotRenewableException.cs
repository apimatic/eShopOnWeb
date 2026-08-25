using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The held authorization has gone stale and PayPal refused to renew it (typically because
/// it is outside the reauthorization window). Surfaced as an operator-actionable message:
/// the order cannot be fulfilled from this authorization and a new payment must be collected.
/// </summary>
public class PaymentAuthorizationNotRenewableException : Exception
{
    public PaymentAuthorizationNotRenewableException(string providerMessage)
        : base($"The payment authorization for this order can no longer be renewed and a new payment must be collected from the shopper. Provider detail: {providerMessage}")
    {
    }
}
