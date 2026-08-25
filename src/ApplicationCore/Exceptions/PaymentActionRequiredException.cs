using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal responded to a direct-card payment with a required payer challenge/redirect
/// (e.g. <c>PAYER_ACTION_REQUIRED</c>). This integration is server-to-server only and does not
/// support a browser approval round-trip, so this is a hard stop rather than something to retry.
/// </summary>
public class PaymentActionRequiredException : Exception
{
    public PaymentActionRequiredException(string message) : base(message)
    {
    }
}
