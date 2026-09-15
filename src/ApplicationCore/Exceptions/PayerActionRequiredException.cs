namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal required a shopper to complete a browser challenge (3-D Secure / payer-action).
/// The task forbids building an approval round-trip; this exception stops the flow.
/// </summary>
public class PayerActionRequiredException : PaymentOperationException
{
    public PayerActionRequiredException(string paypalOrderId, string? paypalDebugId = null)
        : base(409,
            $"PayPal required a browser challenge to complete payment for PayPal order '{paypalOrderId}'. Direct card processing cannot continue without shopper approval in a browser.",
            paypalDebugId)
    {
        PayPalOrderId = paypalOrderId;
    }

    public string PayPalOrderId { get; }
}
