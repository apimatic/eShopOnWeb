namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    public DeletePaymentMethodRequest(int paymentMethodId)
    {
        PaymentMethodId = paymentMethodId;
    }

    public int PaymentMethodId { get; set; }

    /// <summary>Set by the endpoint from the caller's JWT identity - any client-supplied value is ignored.</summary>
    public string BuyerId { get; set; } = string.Empty;
}
