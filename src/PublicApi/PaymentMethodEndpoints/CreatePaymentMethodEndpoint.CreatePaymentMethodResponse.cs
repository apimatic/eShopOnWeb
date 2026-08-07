using System;
using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreatePaymentMethodResponse()
    {
    }

    /// <summary>Identifier of the saved card. Top-level as required by the API contract.</summary>
    public int PaymentMethodId { get; set; }

    /// <summary>Safe description of the saved card so the shopper can recognise it.</summary>
    public SavedCardDto PaymentMethod { get; set; } = new();
}
