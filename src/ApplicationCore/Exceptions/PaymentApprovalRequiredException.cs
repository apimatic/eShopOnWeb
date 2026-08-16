namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve it in a
/// browser (e.g. 3-D Secure). This integration is deliberately server-only and does not build an
/// approval round-trip, so the operation stops and surfaces the challenge instead.
/// </summary>
public class PaymentApprovalRequiredException : PaymentException
{
    public PaymentApprovalRequiredException(string message) : base(message)
    {
    }
}
