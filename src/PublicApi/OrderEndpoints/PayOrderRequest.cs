namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string? VaultToken { get; set; }
    public CardPaymentInfo? Card { get; set; }
    public string BuyerId { get; set; } = "";
}

public class CardPaymentInfo
{
    public string Number { get; set; } = "";
    public string Expiry { get; set; } = "";
    public string? Cvv { get; set; }
    public string? Name { get; set; }
}
