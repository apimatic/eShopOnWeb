using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Alias { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }

    public static PaymentMethodResponse From(PaymentMethod method)
    {
        return new PaymentMethodResponse
        {
            PaymentMethodId = method.Id,
            Alias = method.Alias,
            Brand = method.Brand,
            Last4 = method.Last4,
            Expiry = method.Expiry
        };
    }
}
