namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal responds to a card payment with a challenge that requires the shopper
/// to approve in a browser (for example a 3-D Secure step-up). This integration is designed
/// to be drivable without a browser, so rather than building an approval round-trip we surface
/// this so the caller can stop and report it.
/// </summary>
public class PaymentApprovalRequiredException : PaymentException
{
    public PaymentApprovalRequiredException(string message) : base(message)
    {
    }
}
