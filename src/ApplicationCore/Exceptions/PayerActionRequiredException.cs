using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

// PayPal is asking for a buyer-facing challenge (e.g. 3-D Secure) before it will authorize this
// card. This integration has no browser-redirect step, so this outcome is treated as a hard failure.
public class PayerActionRequiredException : Exception
{
    public PayerActionRequiredException(string message) : base(message)
    {
    }
}
