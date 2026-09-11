using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Wire contracts mirroring the fields this integration uses from the PayPal OpenAPI specs under
// api-specs/paypal. Property names match the spec exactly (snake_case). Only fields the app reads
// or sends are modelled; PayPal returns supersets and ignores absent optional request fields.

internal sealed class MoneyDto
{
    [JsonPropertyName("currency_code")] public string CurrencyCode { get; set; } = string.Empty;
    [JsonPropertyName("value")] public string Value { get; set; } = string.Empty;
}

// ----- checkout orders v2: create order (intent AUTHORIZE) -----

internal sealed class OrderRequestDto
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "AUTHORIZE";
    [JsonPropertyName("purchase_units")] public List<PurchaseUnitRequestDto> PurchaseUnits { get; set; } = new();
    [JsonPropertyName("payment_source")] public PaymentSourceRequestDto? PaymentSource { get; set; }
}

internal sealed class PurchaseUnitRequestDto
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
    [JsonPropertyName("amount")] public MoneyDto Amount { get; set; } = new();
}

internal sealed class PaymentSourceRequestDto
{
    [JsonPropertyName("card")] public CardRequestDto? Card { get; set; }
}

internal sealed class CardRequestDto
{
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("billing_address")] public CardAddressDto? BillingAddress { get; set; }
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
}

internal sealed class CardAddressDto
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
}

// ----- checkout orders v2 / payments v2: responses -----

internal sealed class OrderResponseDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("purchase_units")] public List<PurchaseUnitResponseDto>? PurchaseUnits { get; set; }
    [JsonPropertyName("links")] public List<LinkDto>? Links { get; set; }
}

internal sealed class PurchaseUnitResponseDto
{
    [JsonPropertyName("payments")] public PaymentCollectionDto? Payments { get; set; }
}

internal sealed class PaymentCollectionDto
{
    [JsonPropertyName("authorizations")] public List<AuthorizationDto>? Authorizations { get; set; }
    [JsonPropertyName("captures")] public List<CaptureDto>? Captures { get; set; }
    [JsonPropertyName("refunds")] public List<RefundDto>? Refunds { get; set; }
}

internal sealed class AuthorizationDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public MoneyDto? Amount { get; set; }
    [JsonPropertyName("expiration_time")] public string? ExpirationTime { get; set; }
}

internal sealed class CaptureDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public MoneyDto? Amount { get; set; }
    [JsonPropertyName("seller_receivable_breakdown")] public SellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }
}

internal sealed class SellerReceivableBreakdownDto
{
    [JsonPropertyName("gross_amount")] public MoneyDto? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public MoneyDto? PayPalFee { get; set; }
    [JsonPropertyName("net_amount")] public MoneyDto? NetAmount { get; set; }
}

internal sealed class RefundDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public MoneyDto? Amount { get; set; }
}

internal sealed class LinkDto
{
    [JsonPropertyName("href")] public string? Href { get; set; }
    [JsonPropertyName("rel")] public string? Rel { get; set; }
    [JsonPropertyName("method")] public string? Method { get; set; }
}

// ----- payments v2: request bodies -----

internal sealed class CaptureRequestDto
{
    [JsonPropertyName("amount")] public MoneyDto? Amount { get; set; }
    [JsonPropertyName("final_capture")] public bool FinalCapture { get; set; }
}

internal sealed class ReauthorizeRequestDto
{
    [JsonPropertyName("amount")] public MoneyDto? Amount { get; set; }
}

internal sealed class RefundRequestDto
{
    [JsonPropertyName("amount")] public MoneyDto? Amount { get; set; }
}

// ----- vault payment tokens v3 -----

internal sealed class VaultTokenRequestDto
{
    [JsonPropertyName("payment_source")] public VaultPaymentSourceDto PaymentSource { get; set; } = new();
    [JsonPropertyName("customer")] public CustomerRefDto? Customer { get; set; }
}

internal sealed class VaultPaymentSourceDto
{
    [JsonPropertyName("card")] public CardRequestDto Card { get; set; } = new();
}

internal sealed class CustomerRefDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}

internal sealed class VaultTokenResponseDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("customer")] public CustomerRefDto? Customer { get; set; }
    [JsonPropertyName("payment_source")] public VaultTokenResponseSourceDto? PaymentSource { get; set; }
}

internal sealed class VaultTokenResponseSourceDto
{
    [JsonPropertyName("card")] public VaultCardResponseDto? Card { get; set; }
}

internal sealed class VaultCardResponseDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
}

// ----- transaction search v1 -----

internal sealed class SearchResponseDto
{
    [JsonPropertyName("transaction_details")] public List<TransactionDetailDto>? TransactionDetails { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
    [JsonPropertyName("total_items")] public int TotalItems { get; set; }
}

internal sealed class TransactionDetailDto
{
    [JsonPropertyName("transaction_info")] public TransactionInfoDto? TransactionInfo { get; set; }
}

internal sealed class TransactionInfoDto
{
    [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_field")] public string? CustomField { get; set; }
    [JsonPropertyName("transaction_amount")] public MoneyDto? TransactionAmount { get; set; }
    [JsonPropertyName("fee_amount")] public MoneyDto? FeeAmount { get; set; }
    [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
    [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
    [JsonPropertyName("transaction_initiation_date")] public string? TransactionInitiationDate { get; set; }
}

// ----- error model (shared) -----

internal sealed class ErrorResponseDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
    [JsonPropertyName("details")] public List<ErrorDetailDto>? Details { get; set; }
}

internal sealed class ErrorDetailDto
{
    [JsonPropertyName("field")] public string? Field { get; set; }
    [JsonPropertyName("issue")] public string? Issue { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}

// ----- OAuth token -----

internal sealed class TokenResponseDto
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}
