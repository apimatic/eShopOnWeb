using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Wire models mapped directly to the PayPal OpenAPI specs. Only the fields this integration sends or reads are
// modelled. Property names are pinned explicitly so serialisation matches the spec exactly. Amounts are always
// the money object { currency_code, value } with value as a string.

internal sealed record PpMoney(
    [property: JsonPropertyName("currency_code")] string CurrencyCode,
    [property: JsonPropertyName("value")] string Value);

internal sealed class PpBillingAddress
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }   // city
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }   // state / province
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
}

// ---------- Checkout Orders v2 ----------

internal sealed class PpCard
{
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("billing_address")] public PpBillingAddress? BillingAddress { get; set; }
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
}

internal sealed class PpPaymentSource
{
    [JsonPropertyName("card")] public PpCard? Card { get; set; }
}

internal sealed class PpPurchaseUnitRequest
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
    [JsonPropertyName("amount")] public PpMoney? Amount { get; set; }
}

internal sealed class PpCreateOrderRequest
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "AUTHORIZE";
    [JsonPropertyName("purchase_units")] public List<PpPurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    [JsonPropertyName("payment_source")] public PpPaymentSource? PaymentSource { get; set; }
}

internal sealed class PpLink
{
    [JsonPropertyName("href")] public string? Href { get; set; }
    [JsonPropertyName("rel")] public string? Rel { get; set; }
    [JsonPropertyName("method")] public string? Method { get; set; }
}

internal sealed class PpCardResponse
{
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
}

internal sealed class PpPaymentSourceResponse
{
    [JsonPropertyName("card")] public PpCardResponse? Card { get; set; }
}

internal sealed class PpAuthorization
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("expiration_time")] public string? ExpirationTime { get; set; }
    [JsonPropertyName("amount")] public PpMoney? Amount { get; set; }
}

internal sealed class PpPayments
{
    [JsonPropertyName("authorizations")] public List<PpAuthorization>? Authorizations { get; set; }
}

internal sealed class PpPurchaseUnitResponse
{
    [JsonPropertyName("payments")] public PpPayments? Payments { get; set; }
}

internal sealed class PpOrderResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("purchase_units")] public List<PpPurchaseUnitResponse>? PurchaseUnits { get; set; }
    [JsonPropertyName("payment_source")] public PpPaymentSourceResponse? PaymentSource { get; set; }
    [JsonPropertyName("links")] public List<PpLink>? Links { get; set; }
}

// ---------- Payments v2 ----------

internal sealed class PpCaptureRequest
{
    [JsonPropertyName("amount")] public PpMoney? Amount { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("final_capture")] public bool FinalCapture { get; set; }
}

internal sealed class PpSellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")] public PpMoney? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public PpMoney? PaypalFee { get; set; }
    [JsonPropertyName("net_amount")] public PpMoney? NetAmount { get; set; }
}

internal sealed class PpCaptureResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PpMoney? Amount { get; set; }
    [JsonPropertyName("seller_receivable_breakdown")] public PpSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal sealed class PpAuthorizationResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("expiration_time")] public string? ExpirationTime { get; set; }
    [JsonPropertyName("amount")] public PpMoney? Amount { get; set; }
}

internal sealed class PpReauthorizeRequest
{
    [JsonPropertyName("amount")] public PpMoney? Amount { get; set; }
}

internal sealed class PpRefundRequest
{
    [JsonPropertyName("amount")] public PpMoney? Amount { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
}

internal sealed class PpRefundResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PpMoney? Amount { get; set; }
}

// ---------- Vault v3 ----------

internal sealed class PpCustomer
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}

internal sealed class PpVaultPaymentSource
{
    [JsonPropertyName("card")] public PpCard? Card { get; set; }
}

internal sealed class PpCreatePaymentTokenRequest
{
    [JsonPropertyName("customer")] public PpCustomer? Customer { get; set; }
    [JsonPropertyName("payment_source")] public PpVaultPaymentSource? PaymentSource { get; set; }
}

internal sealed class PpVaultCardResponse
{
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
}

internal sealed class PpVaultPaymentSourceResponse
{
    [JsonPropertyName("card")] public PpVaultCardResponse? Card { get; set; }
}

internal sealed class PpPaymentTokenResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("customer")] public PpCustomer? Customer { get; set; }
    [JsonPropertyName("payment_source")] public PpVaultPaymentSourceResponse? PaymentSource { get; set; }
}

// ---------- Transaction Search v1 ----------

internal sealed class PpTransactionInfo
{
    [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
    [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
    [JsonPropertyName("transaction_initiation_date")] public string? TransactionInitiationDate { get; set; }
    [JsonPropertyName("transaction_amount")] public PpMoney? TransactionAmount { get; set; }
    [JsonPropertyName("fee_amount")] public PpMoney? FeeAmount { get; set; }
    [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_field")] public string? CustomField { get; set; }
    [JsonPropertyName("paypal_reference_id")] public string? PaypalReferenceId { get; set; }
}

internal sealed class PpTransactionDetail
{
    [JsonPropertyName("transaction_info")] public PpTransactionInfo? TransactionInfo { get; set; }
}

internal sealed class PpSearchResponse
{
    [JsonPropertyName("transaction_details")] public List<PpTransactionDetail>? TransactionDetails { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("total_items")] public int TotalItems { get; set; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
}

// ---------- OAuth token ----------

internal sealed class PpTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}

// ---------- Error model ----------

internal sealed class PpErrorDetail
{
    [JsonPropertyName("field")] public string? Field { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("issue")] public string? Issue { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}

internal sealed class PpErrorResponse
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
    [JsonPropertyName("details")] public List<PpErrorDetail>? Details { get; set; }
}
