namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when PayPal answers a card payment with a challenge that would require the shopper to approve
/// in a browser (e.g. 3-D Secure). This integration deliberately does NOT build an approval round-trip;
/// it stops and reports so an operator can act. The sandbox test card does not trigger this.
/// </summary>
public class BrowserApprovalRequiredException : PaymentGatewayException
{
    public BrowserApprovalRequiredException(string message)
        : base(message)
    {
    }
}
