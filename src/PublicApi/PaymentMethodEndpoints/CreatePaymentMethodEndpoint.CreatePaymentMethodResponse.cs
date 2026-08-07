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

    /// <summary>The saved card's id (top-level, so it can be used to pay later or be deleted).</summary>
    public int PaymentMethodId { get; set; }

    public PaymentMethodDto PaymentMethod { get; set; } = new();
}
