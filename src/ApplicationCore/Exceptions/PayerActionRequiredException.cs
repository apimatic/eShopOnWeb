using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal answered a card payment with a challenge (e.g. 3DS) that requires the shopper to
/// approve in a browser. This integration deliberately does not build an approval round-trip;
/// the condition is surfaced to the operator instead.
/// </summary>
public class PayerActionRequiredException : Exception
{
    public PayerActionRequiredException(string message) : base(message)
    {
    }
}
