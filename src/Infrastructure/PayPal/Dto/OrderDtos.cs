using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Dto;

// checkout_orders_v2: POST /v2/checkout/orders, POST /v2/checkout/orders/{id}/authorize

public class OrderCreateRequestDto
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "AUTHORIZE";
    [JsonPropertyName("purchase_units")] public List<PurchaseUnitRequestDto> PurchaseUnits { get; set; } = new();
    [JsonPropertyName("payment_source")] public PaymentSourceRequestDto? PaymentSource { get; set; }
}

public class PurchaseUnitRequestDto
{
    [JsonPropertyName("amount")] public AmountDto Amount { get; set; } = null!;
}

public class PaymentSourceRequestDto
{
    [JsonPropertyName("card")] public CardRequestDto? Card { get; set; }
}

/// <summary>Shared response shape for order-create and order-authorize (both id/status/purchase_units.payments.authorizations[]).</summary>
public class OrderResponseDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = null!;
    [JsonPropertyName("status")] public string Status { get; set; } = null!;
    [JsonPropertyName("purchase_units")] public List<PurchaseUnitResponseDto>? PurchaseUnits { get; set; }
}

public class PurchaseUnitResponseDto
{
    [JsonPropertyName("payments")] public PaymentCollectionDto? Payments { get; set; }
}

public class PaymentCollectionDto
{
    [JsonPropertyName("authorizations")] public List<AuthorizationDto>? Authorizations { get; set; }
    [JsonPropertyName("captures")] public List<CaptureResponseDto>? Captures { get; set; }
}

public class AuthorizationDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = null!;
    [JsonPropertyName("status")] public string Status { get; set; } = null!;
    [JsonPropertyName("amount")] public AmountDto? Amount { get; set; }
    [JsonPropertyName("expiration_time")] public DateTimeOffset? ExpirationTime { get; set; }
    [JsonPropertyName("create_time")] public DateTimeOffset? CreateTime { get; set; }
    [JsonPropertyName("update_time")] public DateTimeOffset? UpdateTime { get; set; }
}
