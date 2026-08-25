namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SaveCardRequest : BaseRequest
{
    public string Number { get; set; } = "";
    public string Expiry { get; set; } = "";
    public string? Cvv { get; set; }
    public string? Name { get; set; }
    public string? Alias { get; set; }
    public string BuyerId { get; set; } = "";
}
