using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListCardsResponse : BaseResponse
{
    public ListCardsResponse(Guid correlationId) : base(correlationId) { }
    public ListCardsResponse() { }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class PaymentMethodDto
{
    public int Id { get; set; }
    public string? Last4 { get; set; }
    public string? CardBrand { get; set; }
    public string? ExpiryMonth { get; set; }
    public string? ExpiryYear { get; set; }
    public string? Alias { get; set; }
}
