using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Wire models mapped field-for-field to the PayPal OpenAPI specs in api-specs/. Property names are
// declared explicitly so the JSON contract does not depend on any naming-policy heuristics.

// ---------- Shared ----------
internal sealed class MoneyModel
{
    [JsonPropertyName("currency_code")] public string? CurrencyCode { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
}

internal sealed class LinkModel
{
    [JsonPropertyName("href")] public string? Href { get; set; }
    [JsonPropertyName("rel")] public string? Rel { get; set; }
    [JsonPropertyName("method")] public string? Method { get; set; }
}

internal sealed class BillingAddressModel
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
}

// ---------- checkout_orders_v2 : create order ----------
internal sealed class CreateOrderRequest
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "AUTHORIZE";
    [JsonPropertyName("purchase_units")] public List<PurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    [JsonPropertyName("payment_source")] public PaymentSourceRequest? PaymentSource { get; set; }
}

internal sealed class PurchaseUnitRequest
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("amount")] public MoneyModel? Amount { get; set; }
}

internal sealed class PaymentSourceRequest
{
    [JsonPropertyName("card")] public CardRequestModel? Card { get; set; }
}

internal sealed class CardRequestModel
{
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
    [JsonPropertyName("billing_address")] public BillingAddressModel? BillingAddress { get; set; }
}

// ---------- Order / authorization responses ----------
internal sealed class OrderResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("purchase_units")] public List<PurchaseUnitResponse>? PurchaseUnits { get; set; }
    [JsonPropertyName("links")] public List<LinkModel>? Links { get; set; }
}

internal sealed class PurchaseUnitResponse
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("payments")] public PaymentCollectionModel? Payments { get; set; }
}

internal sealed class PaymentCollectionModel
{
    [JsonPropertyName("authorizations")] public List<AuthorizationResponse>? Authorizations { get; set; }
    [JsonPropertyName("captures")] public List<CaptureResponse>? Captures { get; set; }
}

internal sealed class AuthorizationResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public MoneyModel? Amount { get; set; }
    [JsonPropertyName("expiration_time")] public string? ExpirationTime { get; set; }
}

// ---------- payments_payment_v2 ----------
internal sealed class CaptureRequestModel
{
    [JsonPropertyName("amount")] public MoneyModel? Amount { get; set; }
    [JsonPropertyName("final_capture")] public bool? FinalCapture { get; set; }
}

internal sealed class ReauthorizeRequestModel
{
    [JsonPropertyName("amount")] public MoneyModel? Amount { get; set; }
}

internal sealed class RefundRequestModel
{
    [JsonPropertyName("amount")] public MoneyModel? Amount { get; set; }
}

internal sealed class CaptureResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public MoneyModel? Amount { get; set; }
    [JsonPropertyName("final_capture")] public bool? FinalCapture { get; set; }
    [JsonPropertyName("seller_receivable_breakdown")] public SellerReceivableBreakdownModel? SellerReceivableBreakdown { get; set; }
}

internal sealed class SellerReceivableBreakdownModel
{
    [JsonPropertyName("gross_amount")] public MoneyModel? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public MoneyModel? PaypalFee { get; set; }
    [JsonPropertyName("net_amount")] public MoneyModel? NetAmount { get; set; }
}

internal sealed class RefundResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public MoneyModel? Amount { get; set; }
}

// ---------- vault_payment_tokens_v3 ----------
internal sealed class VaultCustomerModel
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}

internal sealed class VaultPaymentTokenRequest
{
    [JsonPropertyName("customer")] public VaultCustomerModel? Customer { get; set; }
    [JsonPropertyName("payment_source")] public VaultPaymentSourceModel? PaymentSource { get; set; }
}

internal sealed class SetupTokenRequest
{
    [JsonPropertyName("customer")] public VaultCustomerModel? Customer { get; set; }
    [JsonPropertyName("payment_source")] public VaultPaymentSourceModel? PaymentSource { get; set; }
}

internal sealed class VaultPaymentSourceModel
{
    [JsonPropertyName("card")] public CardRequestModel? Card { get; set; }
    [JsonPropertyName("token")] public TokenIdModel? Token { get; set; }
}

internal sealed class TokenIdModel
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
}

internal sealed class SetupTokenResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("customer")] public VaultCustomerModel? Customer { get; set; }
}

internal sealed class VaultPaymentTokenResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("customer")] public VaultCustomerModel? Customer { get; set; }
    [JsonPropertyName("payment_source")] public VaultPaymentSourceResponseModel? PaymentSource { get; set; }
}

internal sealed class VaultPaymentSourceResponseModel
{
    [JsonPropertyName("card")] public CardResponseModel? Card { get; set; }
}

internal sealed class CardResponseModel
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
}

// ---------- transaction_search_v1 ----------
internal sealed class SearchResponse
{
    [JsonPropertyName("transaction_details")] public List<TransactionDetailModel>? TransactionDetails { get; set; }
    [JsonPropertyName("page")] public int? Page { get; set; }
    [JsonPropertyName("total_items")] public int? TotalItems { get; set; }
    [JsonPropertyName("total_pages")] public int? TotalPages { get; set; }
}

internal sealed class TransactionDetailModel
{
    [JsonPropertyName("transaction_info")] public TransactionInfoModel? TransactionInfo { get; set; }
}

internal sealed class TransactionInfoModel
{
    [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
    [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
    [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
    [JsonPropertyName("transaction_initiation_date")] public string? TransactionInitiationDate { get; set; }
    [JsonPropertyName("transaction_amount")] public MoneyModel? TransactionAmount { get; set; }
    [JsonPropertyName("fee_amount")] public MoneyModel? FeeAmount { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_field")] public string? CustomField { get; set; }
    [JsonPropertyName("paypal_reference_id")] public string? PayPalReferenceId { get; set; }
    [JsonPropertyName("payment_method_type")] public string? PaymentMethodType { get; set; }
    [JsonPropertyName("instrument_type")] public string? InstrumentType { get; set; }
}
