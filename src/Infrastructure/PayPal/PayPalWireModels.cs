using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Wire models matching the PayPal OpenAPI specifications in api-specs/paypal
// (checkout_orders_v2, payments_payment_v2, vault_payment_tokens_v3, transaction_search_v1).
// Property names are bound explicitly to the spec's field names.

internal class TokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}

internal class MoneyWire
{
    public MoneyWire() { }
    public MoneyWire(string currencyCode, string value) { CurrencyCode = currencyCode; Value = value; }

    [JsonPropertyName("currency_code")] public string CurrencyCode { get; set; } = string.Empty;
    [JsonPropertyName("value")] public string Value { get; set; } = string.Empty;
}

internal class AddressWire
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string CountryCode { get; set; } = string.Empty;
}

internal class StoredCredentialWire
{
    [JsonPropertyName("payment_initiator")] public string PaymentInitiator { get; set; } = "CUSTOMER";
    [JsonPropertyName("payment_type")] public string PaymentType { get; set; } = "UNSCHEDULED";
    [JsonPropertyName("usage")] public string Usage { get; set; } = "SUBSEQUENT";
}

internal class CardRequestWire
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("billing_address")] public AddressWire? BillingAddress { get; set; }
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
    [JsonPropertyName("stored_credential")] public StoredCredentialWire? StoredCredential { get; set; }
}

internal class PaymentSourceRequestWire
{
    [JsonPropertyName("card")] public CardRequestWire? Card { get; set; }
}

internal class PurchaseUnitRequestWire
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("amount")] public MoneyWire Amount { get; set; } = new();
}

internal class CreateOrderRequestWire
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "AUTHORIZE";
    [JsonPropertyName("purchase_units")] public List<PurchaseUnitRequestWire> PurchaseUnits { get; set; } = new();
    [JsonPropertyName("payment_source")] public PaymentSourceRequestWire? PaymentSource { get; set; }
}

internal class CardResponseWire
{
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal class PaymentSourceResponseWire
{
    [JsonPropertyName("card")] public CardResponseWire? Card { get; set; }
}

internal class AuthorizationWire
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public MoneyWire? Amount { get; set; }
    [JsonPropertyName("expiration_time")] public string? ExpirationTime { get; set; }
}

internal class SellerReceivableBreakdownWire
{
    [JsonPropertyName("gross_amount")] public MoneyWire? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public MoneyWire? PayPalFee { get; set; }
    [JsonPropertyName("net_amount")] public MoneyWire? NetAmount { get; set; }
}

internal class CaptureWire
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public MoneyWire? Amount { get; set; }
    [JsonPropertyName("final_capture")] public bool FinalCapture { get; set; }
    [JsonPropertyName("seller_receivable_breakdown")] public SellerReceivableBreakdownWire? SellerReceivableBreakdown { get; set; }
}

internal class RefundWire
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public MoneyWire? Amount { get; set; }
}

internal class PaymentCollectionWire
{
    [JsonPropertyName("authorizations")] public List<AuthorizationWire>? Authorizations { get; set; }
    [JsonPropertyName("captures")] public List<CaptureWire>? Captures { get; set; }
    [JsonPropertyName("refunds")] public List<RefundWire>? Refunds { get; set; }
}

internal class PurchaseUnitResponseWire
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("payments")] public PaymentCollectionWire? Payments { get; set; }
}

internal class OrderResponseWire
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("intent")] public string? Intent { get; set; }
    [JsonPropertyName("payment_source")] public PaymentSourceResponseWire? PaymentSource { get; set; }
    [JsonPropertyName("purchase_units")] public List<PurchaseUnitResponseWire>? PurchaseUnits { get; set; }
}

internal class CaptureRequestWire
{
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("final_capture")] public bool FinalCapture { get; set; } = true;
}

internal class ReauthorizeRequestWire
{
    [JsonPropertyName("amount")] public MoneyWire Amount { get; set; } = new();
}

internal class RefundRequestWire
{
    [JsonPropertyName("amount")] public MoneyWire? Amount { get; set; }
    [JsonPropertyName("note_to_payer")] public string? NoteToPayer { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
}

internal class VaultCustomerWire
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
}

internal class VaultPaymentSourceWire
{
    [JsonPropertyName("card")] public CardRequestWire? Card { get; set; }
}

internal class VaultTokenRequestWire
{
    [JsonPropertyName("customer")] public VaultCustomerWire? Customer { get; set; }
    [JsonPropertyName("payment_source")] public VaultPaymentSourceWire PaymentSource { get; set; } = new();
}

internal class VaultTokenResponseWire
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("payment_source")] public PaymentSourceResponseWire? PaymentSource { get; set; }
}

internal class TransactionInfoWire
{
    [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
    [JsonPropertyName("paypal_reference_id")] public string? PayPalReferenceId { get; set; }
    [JsonPropertyName("paypal_reference_id_type")] public string? PayPalReferenceIdType { get; set; }
    [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
    [JsonPropertyName("transaction_initiation_date")] public string? TransactionInitiationDate { get; set; }
    [JsonPropertyName("transaction_amount")] public MoneyWire? TransactionAmount { get; set; }
    [JsonPropertyName("fee_amount")] public MoneyWire? FeeAmount { get; set; }
    [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_field")] public string? CustomField { get; set; }
}

internal class TransactionDetailWire
{
    [JsonPropertyName("transaction_info")] public TransactionInfoWire? TransactionInfo { get; set; }
}

internal class TransactionSearchResponseWire
{
    [JsonPropertyName("transaction_details")] public List<TransactionDetailWire>? TransactionDetails { get; set; }
    [JsonPropertyName("total_items")] public int TotalItems { get; set; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
}

internal class ErrorDetailWire
{
    [JsonPropertyName("issue")] public string? Issue { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}

internal class ErrorResponseWire
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
    [JsonPropertyName("details")] public List<ErrorDetailWire>? Details { get; set; }
}
