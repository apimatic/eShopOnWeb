using System;
using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    /// <summary>The saved card's id (top-level, so callers can reference it when paying).</summary>
    public int PaymentMethodId { get; set; }

    /// <summary>A safe descriptor of the saved card (brand, last four digits, expiry).</summary>
    public SavedCardDto? PaymentMethod { get; set; }
}
