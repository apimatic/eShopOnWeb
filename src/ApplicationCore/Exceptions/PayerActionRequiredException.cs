using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal answered a card payment with a challenge (e.g. 3-D Secure) that requires the
/// shopper to approve in a browser. This integration is server-to-server only.
/// </summary>
public class PayerActionRequiredException : Exception
{
    public PayerActionRequiredException(string message) : base(message)
    {
    }
}
