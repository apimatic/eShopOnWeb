using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public record ListPaymentMethodsRequest
{
    public string BuyerId { get; init; } = "";
}

public record ListPaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; init; } = new();
}

public record PaymentMethodDto
{
    public int PaymentMethodId { get; init; }
    public string? CardBrand { get; init; }
    public string? Last4 { get; init; }
    public string? CardExpiry { get; init; }
    public string? CardholderName { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
