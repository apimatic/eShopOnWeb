using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Wire DTOs for the PayPal Payments API. Names match the PayPal JSON exactly.
// Request DTOs carrying card details are serialized straight to PayPal and are
// never written to logs.

internal sealed class PayPalTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
}

internal sealed class PayPalMoneyDto
{
    public PayPalMoneyDto() { }
    public PayPalMoneyDto(string currencyCode, string value)
    {
        CurrencyCode = currencyCode;
        Value = value;
    }

    [JsonPropertyName("currency_code")] public string? CurrencyCode { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
}

internal sealed class PayPalLinkDto
{
    [JsonPropertyName("href")] public string? Href { get; set; }
    [JsonPropertyName("rel")] public string? Rel { get; set; }
    [JsonPropertyName("method")] public string? Method { get; set; }
}

internal sealed class PayPalAddressDto
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
}

// ----- Orders v2 -----

internal sealed class CreateOrderRequestDto
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "AUTHORIZE";
    [JsonPropertyName("purchase_units")] public List<PurchaseUnitRequestDto> PurchaseUnits { get; set; } = new();
}

internal sealed class PurchaseUnitRequestDto
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
    [JsonPropertyName("amount")] public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class AuthorizeOrderRequestDto
{
    [JsonPropertyName("payment_source")] public PaymentSourceRequestDto? PaymentSource { get; set; }
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
    [JsonPropertyName("billing_address")] public PayPalAddressDto? BillingAddress { get; set; }
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
    [JsonPropertyName("stored_credential")] public StoredCredentialDto? StoredCredential { get; set; }
}

internal sealed class StoredCredentialDto
{
    [JsonPropertyName("payment_initiator")] public string? PaymentInitiator { get; set; }
    [JsonPropertyName("payment_type")] public string? PaymentType { get; set; }
}

internal sealed class OrderResponseDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("purchase_units")] public List<PurchaseUnitResponseDto>? PurchaseUnits { get; set; }
    [JsonPropertyName("links")] public List<PayPalLinkDto>? Links { get; set; }
}

internal sealed class PurchaseUnitResponseDto
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("payments")] public PaymentCollectionDto? Payments { get; set; }
}

internal sealed class PaymentCollectionDto
{
    [JsonPropertyName("authorizations")] public List<AuthorizationDto>? Authorizations { get; set; }
    [JsonPropertyName("captures")] public List<CaptureDto>? Captures { get; set; }
    [JsonPropertyName("refunds")] public List<RefundDto>? Refunds { get; set; }
}

// ----- Payments v2 -----

internal sealed class AuthorizationDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoneyDto? Amount { get; set; }
    [JsonPropertyName("expiration_time")] public string? ExpirationTime { get; set; }
    [JsonPropertyName("create_time")] public string? CreateTime { get; set; }
    [JsonPropertyName("update_time")] public string? UpdateTime { get; set; }
    [JsonPropertyName("links")] public List<PayPalLinkDto>? Links { get; set; }
}

internal sealed class CaptureRequestDto
{
    [JsonPropertyName("amount")] public PayPalMoneyDto? Amount { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("final_capture")] public bool FinalCapture { get; set; }
}

internal sealed class ReauthorizeRequestDto
{
    [JsonPropertyName("amount")] public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class CaptureDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoneyDto? Amount { get; set; }
    [JsonPropertyName("final_capture")] public bool? FinalCapture { get; set; }
    [JsonPropertyName("seller_receivable_breakdown")] public SellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }
    [JsonPropertyName("create_time")] public string? CreateTime { get; set; }
    [JsonPropertyName("update_time")] public string? UpdateTime { get; set; }
}

internal sealed class SellerReceivableBreakdownDto
{
    [JsonPropertyName("gross_amount")] public PayPalMoneyDto? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public PayPalMoneyDto? PayPalFee { get; set; }
    [JsonPropertyName("net_amount")] public PayPalMoneyDto? NetAmount { get; set; }
}

internal sealed class RefundRequestDto
{
    [JsonPropertyName("amount")] public PayPalMoneyDto? Amount { get; set; }
    [JsonPropertyName("note_to_payer")] public string? NoteToPayer { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
}

internal sealed class RefundDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoneyDto? Amount { get; set; }
    [JsonPropertyName("note_to_payer")] public string? NoteToPayer { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
    [JsonPropertyName("seller_payable_breakdown")] public SellerPayableBreakdownDto? SellerPayableBreakdown { get; set; }
    [JsonPropertyName("create_time")] public string? CreateTime { get; set; }
}

internal sealed class SellerPayableBreakdownDto
{
    [JsonPropertyName("gross_amount")] public PayPalMoneyDto? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public PayPalMoneyDto? PayPalFee { get; set; }
    [JsonPropertyName("net_amount")] public PayPalMoneyDto? NetAmount { get; set; }
    [JsonPropertyName("total_refunded_amount")] public PayPalMoneyDto? TotalRefundedAmount { get; set; }
}

// ----- Vault v3 -----

internal sealed class SetupTokenRequestDto
{
    [JsonPropertyName("customer")] public CustomerDto? Customer { get; set; }
    [JsonPropertyName("payment_source")] public SetupTokenPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class CustomerDto
{
    public CustomerDto() { }
    public CustomerDto(string id) { Id = id; }

    [JsonPropertyName("id")] public string? Id { get; set; }
}

internal sealed class SetupTokenPaymentSourceDto
{
    [JsonPropertyName("card")] public CardRequestDto? Card { get; set; }
}

internal sealed class SetupTokenResponseDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("links")] public List<PayPalLinkDto>? Links { get; set; }
}

internal sealed class PaymentTokenRequestDto
{
    [JsonPropertyName("customer")] public CustomerDto? Customer { get; set; }
    [JsonPropertyName("payment_source")] public PaymentTokenSourceRequestDto? PaymentSource { get; set; }
}

internal sealed class PaymentTokenSourceRequestDto
{
    [JsonPropertyName("token")] public TokenIdDto? Token { get; set; }
}

internal sealed class TokenIdDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
}

internal sealed class PaymentTokenResponseDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("customer")] public CustomerDto? Customer { get; set; }
    [JsonPropertyName("payment_source")] public PaymentTokenSourceDto? PaymentSource { get; set; }
}

internal sealed class PaymentTokenSourceDto
{
    [JsonPropertyName("card")] public VaultedCardDto? Card { get; set; }
}

internal sealed class VaultedCardDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
}

// ----- Transaction Search v1 -----

internal sealed class TransactionSearchResponseDto
{
    [JsonPropertyName("transaction_details")] public List<TransactionDetailDto>? TransactionDetails { get; set; }
    [JsonPropertyName("total_items")] public int TotalItems { get; set; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
}

internal sealed class TransactionDetailDto
{
    [JsonPropertyName("transaction_info")] public TransactionInfoDto? TransactionInfo { get; set; }
}

internal sealed class TransactionInfoDto
{
    [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
    [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
    [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
    [JsonPropertyName("transaction_amount")] public PayPalMoneyDto? TransactionAmount { get; set; }
    [JsonPropertyName("fee_amount")] public PayPalMoneyDto? FeeAmount { get; set; }
    [JsonPropertyName("transaction_initiation_date")] public string? TransactionInitiationDate { get; set; }
    [JsonPropertyName("transaction_updated_date")] public string? TransactionUpdatedDate { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_field")] public string? CustomField { get; set; }
    [JsonPropertyName("paypal_reference_id")] public string? PayPalReferenceId { get; set; }
}

// ----- Errors -----

internal sealed class PayPalErrorDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
    [JsonPropertyName("details")] public List<PayPalErrorDetailDto>? Details { get; set; }
}

internal sealed class PayPalErrorDetailDto
{
    [JsonPropertyName("issue")] public string? Issue { get; set; }
    [JsonPropertyName("field")] public string? Field { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}
