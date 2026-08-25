using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Dto;

// payments_payment_v2: GET/reauthorize/void authorizations, capture, refund

public class AuthorizationDetailDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = null!;
    [JsonPropertyName("status")] public string Status { get; set; } = null!;
    [JsonPropertyName("amount")] public AmountDto? Amount { get; set; }
    [JsonPropertyName("expiration_time")] public DateTimeOffset? ExpirationTime { get; set; }
    [JsonPropertyName("create_time")] public DateTimeOffset? CreateTime { get; set; }
    [JsonPropertyName("update_time")] public DateTimeOffset? UpdateTime { get; set; }
}

public class ReauthorizeRequestDto
{
    [JsonPropertyName("amount")] public AmountDto? Amount { get; set; }
}

public class CaptureRequestDto
{
    [JsonPropertyName("amount")] public AmountDto? Amount { get; set; }
    [JsonPropertyName("final_capture")] public bool FinalCapture { get; set; }
}

public class CaptureResponseDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = null!;
    [JsonPropertyName("status")] public string Status { get; set; } = null!;
    [JsonPropertyName("amount")] public AmountDto? Amount { get; set; }
    [JsonPropertyName("final_capture")] public bool FinalCapture { get; set; }
    [JsonPropertyName("seller_receivable_breakdown")] public SellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }
    [JsonPropertyName("create_time")] public DateTimeOffset? CreateTime { get; set; }
    [JsonPropertyName("update_time")] public DateTimeOffset? UpdateTime { get; set; }
}

public class SellerReceivableBreakdownDto
{
    [JsonPropertyName("gross_amount")] public AmountDto? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public AmountDto? PayPalFee { get; set; }
    [JsonPropertyName("net_amount")] public AmountDto? NetAmount { get; set; }
}

public class RefundRequestDto
{
    [JsonPropertyName("amount")] public AmountDto? Amount { get; set; }
    [JsonPropertyName("note_to_payer")] public string? NoteToPayer { get; set; }
}

public class RefundResponseDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = null!;
    [JsonPropertyName("status")] public string Status { get; set; } = null!;
    [JsonPropertyName("amount")] public AmountDto? Amount { get; set; }
}
