using System;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SaveCardResponse : BaseResponse
{
    public SaveCardResponse(Guid correlationId) : base(correlationId) { }
    public SaveCardResponse() { }

    public int PaymentMethodId { get; set; }
    public string? Last4 { get; set; }
    public string? CardBrand { get; set; }
    public string? ExpiryMonth { get; set; }
    public string? ExpiryYear { get; set; }
}
