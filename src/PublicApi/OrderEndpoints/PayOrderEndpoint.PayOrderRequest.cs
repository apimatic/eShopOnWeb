namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int? PaymentMethodId { get; set; }
    public CardInput? Card { get; set; }
}

public class CardInput
{
    public string Number { get; set; } = "";
    public string Expiry { get; set; } = "";
    public string SecurityCode { get; set; } = "";
    public string? Name { get; set; }
}
