namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class DeletePaymentMethodRequest
{
    public DeletePaymentMethodRequest(int paymentMethodId)
    {
        PaymentMethodId = paymentMethodId;
    }

    public int PaymentMethodId { get; }
}
