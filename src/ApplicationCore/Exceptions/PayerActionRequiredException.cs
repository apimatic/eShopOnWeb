using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a
/// browser (e.g. 3-D Secure). This integration is server-to-server only and does not build an
/// approval round-trip, so the payment cannot proceed.
/// </summary>
public class PayerActionRequiredException : Exception
{
    public PayerActionRequiredException(string message) : base(message)
    {
    }
}
