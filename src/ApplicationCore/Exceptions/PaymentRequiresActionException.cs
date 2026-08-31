using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal answered a card payment with a challenge (e.g. 3-D Secure) that requires
/// the shopper to approve in a browser. This integration does not support approval
/// round-trips; the payment attempt is rejected instead.
/// </summary>
public class PaymentRequiresActionException : Exception
{
    public PaymentRequiresActionException(string message) : base(message)
    {
    }
}
