using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when PayPal responds to a card payment with a challenge that requires the
/// shopper to complete an approval step in a browser (e.g. 3-D Secure step-up). This integration
/// is direct-card, server-to-server only and does not implement a payer-approval round trip.</summary>
public class PayPalActionRequiredException : Exception
{
    public PayPalActionRequiredException(string message) : base(message)
    {
    }
}
