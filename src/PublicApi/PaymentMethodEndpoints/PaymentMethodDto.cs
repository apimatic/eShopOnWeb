namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = "";
    public string Last4 { get; set; } = "";
    public string ExpiryYearMonth { get; set; } = "";
    public string? Alias { get; set; }

    /// <summary>A safe, human-recognisable description, e.g. "VISA ending 1111 (expires 2028-04)".</summary>
    public string Description { get; set; } = "";
}
