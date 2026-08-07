using System;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SavePaymentMethodResponse()
    {
    }

    /// <summary>Identifier of the saved card (top-level, so callers can pay with it later).</summary>
    public int PaymentMethodId { get; set; }

    public PaymentMethodDto PaymentMethod { get; set; } = new();
}
