namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public record DeletePaymentMethodRequest
{
    public int PaymentMethodId { get; init; }
    public string BuyerId { get; init; } = "";
}
