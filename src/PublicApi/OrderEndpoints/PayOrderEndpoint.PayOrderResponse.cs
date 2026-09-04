using System;
using Microsoft.eShopWeb.PublicApi.PaymentDtos;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse() { }

    public PayOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>False when this call actually placed the hold; true for an idempotent replay.</summary>
    public bool Replayed { get; set; }

    public PaymentDto? Payment { get; set; }
}
