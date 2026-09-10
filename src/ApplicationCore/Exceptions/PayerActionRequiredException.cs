using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that requires the shopper to approve it
/// in a browser (e.g. 3-D Secure). This integration deliberately does not build a browser approval
/// round-trip; it surfaces the challenge so an operator can see it.
/// </summary>
public class PayerActionRequiredException : Exception
{
    public PayerActionRequiredException(string message) : base(message) { }
}
