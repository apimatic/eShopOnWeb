using System;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreatePaymentMethodResponse()
    {
    }

    /// <summary>Top-level identifier of the saved card, so it can be referenced when paying.</summary>
    public int PaymentMethodId { get; set; }

    public PaymentMethodDto PaymentMethod { get; set; } = new();
}
