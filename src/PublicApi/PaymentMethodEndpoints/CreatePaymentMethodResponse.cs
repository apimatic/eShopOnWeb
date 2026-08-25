using System;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public int PaymentMethodId { get; set; }
    public string? LastFour { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}
