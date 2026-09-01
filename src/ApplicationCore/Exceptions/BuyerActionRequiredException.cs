using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal answered a card payment with a challenge (e.g. 3DS) that requires the shopper to
/// approve in a browser. This integration is server-to-server only, so the payment cannot
/// proceed; the order remains awaiting payment.
/// </summary>
public class BuyerActionRequiredException : Exception
{
    public BuyerActionRequiredException(string message) : base(message)
    {
    }
}
