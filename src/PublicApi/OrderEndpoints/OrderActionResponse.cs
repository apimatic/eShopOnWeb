using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderActionResponse : BaseResponse
{
    public OrderActionResponse(Guid correlationId) : base(correlationId) {}
    public OrderActionResponse() {}

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long TotalAmountCents { get; set; }
    public string? Currency { get; set; }
    public PaymentStateDto Payment { get; set; } = new();
    public List<RefundDto> Refunds { get; set; } = new();
}
