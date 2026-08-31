using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Dto;

// Wire DTOs for the PayPal REST APIs (Orders v2, Payments v2, Vault v3, Reporting v1).
// Property names match PayPal's snake_case JSON exactly.

internal class PayPalAmount
{
    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = string.Empty;
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

internal class PayPalAddress
{
    [JsonPropertyName("address_line_1")]
    public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")]
    public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")]
    public string? AdminArea2 { get; set; }
    [JsonPropertyName("admin_area_1")]
    public string? AdminArea1 { get; set; }
    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }
}

internal class PayPalCard
{
    [JsonPropertyName("number")]
    public string? Number { get; set; }
    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }
    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("billing_address")]
    public PayPalAddress? BillingAddress { get; set; }
    [JsonPropertyName("vault_id")]
    public string? VaultId { get; set; }

    // Response-only, safe display data
    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }
    [JsonPropertyName("brand")]
    public string? Brand { get; set; }
}

internal class PayPalPaymentSource
{
    [JsonPropertyName("card")]
    public PayPalCard? Card { get; set; }
    [JsonPropertyName("token")]
    public PayPalTokenSource? Token { get; set; }
}

internal class PayPalTokenSource
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

internal class PayPalPurchaseUnitRequest
{
    [JsonPropertyName("reference_id")]
    public string? ReferenceId { get; set; }
    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }
    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }
    [JsonPropertyName("amount")]
    public PayPalAmount Amount { get; set; } = new();
}

internal class PayPalCreateOrderRequest
{
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "AUTHORIZE";
    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    [JsonPropertyName("payment_source")]
    public PayPalPaymentSource? PaymentSource { get; set; }
}

internal class PayPalSellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")]
    public PayPalAmount? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")]
    public PayPalAmount? PayPalFee { get; set; }
    [JsonPropertyName("net_amount")]
    public PayPalAmount? NetAmount { get; set; }
}

internal class PayPalAuthorization
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    [JsonPropertyName("amount")]
    public PayPalAmount? Amount { get; set; }
    [JsonPropertyName("expiration_time")]
    public string? ExpirationTime { get; set; }
    [JsonPropertyName("create_time")]
    public string? CreateTime { get; set; }
}

internal class PayPalCapture
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    [JsonPropertyName("amount")]
    public PayPalAmount? Amount { get; set; }
    [JsonPropertyName("final_capture")]
    public bool FinalCapture { get; set; }
    [JsonPropertyName("seller_receivable_breakdown")]
    public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
    [JsonPropertyName("create_time")]
    public string? CreateTime { get; set; }
}

internal class PayPalPayments
{
    [JsonPropertyName("authorizations")]
    public List<PayPalAuthorization>? Authorizations { get; set; }
    [JsonPropertyName("captures")]
    public List<PayPalCapture>? Captures { get; set; }
}

internal class PayPalPurchaseUnitResponse
{
    [JsonPropertyName("reference_id")]
    public string? ReferenceId { get; set; }
    [JsonPropertyName("payments")]
    public PayPalPayments? Payments { get; set; }
}

internal class PayPalOrderResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    [JsonPropertyName("payment_source")]
    public PayPalPaymentSource? PaymentSource { get; set; }
    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnitResponse>? PurchaseUnits { get; set; }
}

internal class PayPalCaptureRequest
{
    [JsonPropertyName("amount")]
    public PayPalAmount Amount { get; set; } = new();
    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }
    [JsonPropertyName("final_capture")]
    public bool FinalCapture { get; set; } = true;
}

internal class PayPalReauthorizeRequest
{
    [JsonPropertyName("amount")]
    public PayPalAmount Amount { get; set; } = new();
}

internal class PayPalRefundRequest
{
    [JsonPropertyName("amount")]
    public PayPalAmount? Amount { get; set; }
    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }
    [JsonPropertyName("note_to_payer")]
    public string? NoteToPayer { get; set; }
    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }
}

internal class PayPalRefundResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    [JsonPropertyName("amount")]
    public PayPalAmount? Amount { get; set; }
}

internal class PayPalCustomer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

internal class PayPalSetupTokenRequest
{
    [JsonPropertyName("payment_source")]
    public PayPalPaymentSource PaymentSource { get; set; } = new();
    [JsonPropertyName("customer")]
    public PayPalCustomer? Customer { get; set; }
}

internal class PayPalSetupTokenResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    [JsonPropertyName("customer")]
    public PayPalCustomer? Customer { get; set; }
    [JsonPropertyName("payment_source")]
    public PayPalPaymentSource? PaymentSource { get; set; }
}

internal class PayPalCreatePaymentTokenRequest
{
    [JsonPropertyName("payment_source")]
    public PayPalPaymentSource PaymentSource { get; set; } = new();
}

internal class PayPalPaymentTokenResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("customer")]
    public PayPalCustomer? Customer { get; set; }
    [JsonPropertyName("payment_source")]
    public PayPalPaymentSource? PaymentSource { get; set; }
}

internal class PayPalOAuthResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal class PayPalErrorDetail
{
    [JsonPropertyName("issue")]
    public string? Issue { get; set; }
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal class PayPalErrorResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("message")]
    public string? Message { get; set; }
    [JsonPropertyName("debug_id")]
    public string? DebugId { get; set; }
    [JsonPropertyName("details")]
    public List<PayPalErrorDetail>? Details { get; set; }
}

internal class PayPalTransactionInfo
{
    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; set; }
    [JsonPropertyName("paypal_reference_id")]
    public string? PayPalReferenceId { get; set; }
    [JsonPropertyName("paypal_reference_id_type")]
    public string? PayPalReferenceIdType { get; set; }
    [JsonPropertyName("transaction_event_code")]
    public string? TransactionEventCode { get; set; }
    [JsonPropertyName("transaction_initiation_date")]
    public string? TransactionInitiationDate { get; set; }
    [JsonPropertyName("transaction_updated_date")]
    public string? TransactionUpdatedDate { get; set; }
    [JsonPropertyName("transaction_amount")]
    public PayPalAmount? TransactionAmount { get; set; }
    [JsonPropertyName("fee_amount")]
    public PayPalAmount? FeeAmount { get; set; }
    [JsonPropertyName("transaction_status")]
    public string? TransactionStatus { get; set; }
}

internal class PayPalTransactionDetail
{
    [JsonPropertyName("transaction_info")]
    public PayPalTransactionInfo? TransactionInfo { get; set; }
}

internal class PayPalTransactionSearchResponse
{
    [JsonPropertyName("transaction_details")]
    public List<PayPalTransactionDetail>? TransactionDetails { get; set; }
    [JsonPropertyName("page")]
    public int Page { get; set; }
    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }
    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }
}
