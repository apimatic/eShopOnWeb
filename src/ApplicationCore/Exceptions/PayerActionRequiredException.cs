namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PayerActionRequiredException : OrderPaymentException
{
    public PayerActionRequiredException(string? payPalOrderId, string? debugId)
        : base(409,
            "PayPal required a shopper approval step (for example 3-D Secure) that cannot be completed without a browser. " +
            "This integration does not collect a payer-action round-trip. " +
            $"PayPal order id: {payPalOrderId ?? "(none)"}; debug id: {debugId ?? "(none)"}.")
    {
        PayPalOrderId = payPalOrderId;
        DebugId = debugId;
    }

    public string? PayPalOrderId { get; }
    public string? DebugId { get; }
}
