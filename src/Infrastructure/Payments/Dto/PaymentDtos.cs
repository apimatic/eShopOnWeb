using System;
using System.Text.Json.Serialization;

// DTOs for the Payments API v2 (api-specs/paypal/payments_payment_v2).
namespace Microsoft.eShopWeb.Infrastructure.Payments.Dto;

/// <summary>authorization schema (read side, subset).</summary>
public class PayPalAuthorization
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoney? Amount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("expiration_time")]
    public DateTimeOffset? ExpirationTime { get; set; }

    [JsonPropertyName("create_time")]
    public DateTimeOffset? CreateTime { get; set; }

    [JsonPropertyName("update_time")]
    public DateTimeOffset? UpdateTime { get; set; }
}

/// <summary>capture_request schema.</summary>
public class PayPalCaptureRequest
{
    [JsonPropertyName("amount")]
    public PayPalMoney? Amount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("final_capture")]
    public bool? FinalCapture { get; set; }

    [JsonPropertyName("note_to_payer")]
    public string? NoteToPayer { get; set; }
}

/// <summary>reauthorize_request schema.</summary>
public class PayPalReauthorizeRequest
{
    [JsonPropertyName("amount")]
    public PayPalMoney Amount { get; set; } = new();
}

/// <summary>capture schema (read side, subset).</summary>
public class PayPalCapture
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoney? Amount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("final_capture")]
    public bool? FinalCapture { get; set; }

    [JsonPropertyName("seller_receivable_breakdown")]
    public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }

    [JsonPropertyName("create_time")]
    public DateTimeOffset? CreateTime { get; set; }

    [JsonPropertyName("update_time")]
    public DateTimeOffset? UpdateTime { get; set; }
}

/// <summary>seller_receivable_breakdown schema.</summary>
public class PayPalSellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")]
    public PayPalMoney? GrossAmount { get; set; }

    [JsonPropertyName("paypal_fee")]
    public PayPalMoney? PayPalFee { get; set; }

    [JsonPropertyName("net_amount")]
    public PayPalMoney? NetAmount { get; set; }
}

/// <summary>refund_request schema.</summary>
public class PayPalRefundRequest
{
    [JsonPropertyName("amount")]
    public PayPalMoney? Amount { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("note_to_payer")]
    public string? NoteToPayer { get; set; }
}

/// <summary>refund schema (read side, subset).</summary>
public class PayPalRefund
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoney? Amount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("note_to_payer")]
    public string? NoteToPayer { get; set; }

    [JsonPropertyName("create_time")]
    public DateTimeOffset? CreateTime { get; set; }

    [JsonPropertyName("update_time")]
    public DateTimeOffset? UpdateTime { get; set; }
}
