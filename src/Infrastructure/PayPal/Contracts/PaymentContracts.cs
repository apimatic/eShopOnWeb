using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;

// --- Authorizations (payments_payment_v2) ---

public class AuthorizationResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public Money? Amount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("expiration_time")]
    public string? ExpirationTime { get; set; }
}

public class CaptureRequest
{
    [JsonPropertyName("amount")]
    public Money? Amount { get; set; }

    [JsonPropertyName("final_capture")]
    public bool? FinalCapture { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }
}

public class ReauthorizeRequest
{
    [JsonPropertyName("amount")]
    public Money? Amount { get; set; }
}

public class CaptureResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public Money? Amount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("final_capture")]
    public bool? FinalCapture { get; set; }

    [JsonPropertyName("seller_receivable_breakdown")]
    public SellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

public class SellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")]
    public Money? GrossAmount { get; set; }

    [JsonPropertyName("paypal_fee")]
    public Money? PaypalFee { get; set; }

    [JsonPropertyName("net_amount")]
    public Money? NetAmount { get; set; }
}

// --- Refunds (payments_payment_v2) ---

public class RefundRequest
{
    [JsonPropertyName("amount")]
    public Money? Amount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("note_to_payer")]
    public string? NoteToPayer { get; set; }
}

public class RefundResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public Money? Amount { get; set; }
}
