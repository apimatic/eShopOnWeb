using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// These types mirror — field for field — the parts of the PayPal OpenAPI specifications in
// api-specs/paypal that this integration uses. Property names are pinned with [JsonPropertyName]
// so they match the spec exactly (e.g. "admin_area_1"), rather than relying on a naming policy.
// Only the fields the integration reads or sends are modelled; the spec remains the contract.

// ---- money (shared) -------------------------------------------------------------------------
internal sealed class MoneyDto
{
    [JsonPropertyName("currency_code")] public string CurrencyCode { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}

internal sealed class AddressDto
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
}

// ---- checkout orders v2: create order request ----------------------------------------------
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
    [JsonPropertyName("amount")] public MoneyDto Amount { get; set; } = new();
}

internal sealed class PaymentSourceRequest
{
    [JsonPropertyName("card")] public CardRequest? Card { get; set; }
}

internal sealed class CardRequest
{
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("billing_address")] public AddressDto? BillingAddress { get; set; }
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
}

// ---- checkout orders v2: order response -----------------------------------------------------
internal sealed class OrderResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("payment_source")] public PaymentSourceResponse? PaymentSource { get; set; }
    [JsonPropertyName("purchase_units")] public List<PurchaseUnitResponse>? PurchaseUnits { get; set; }
}

internal sealed class PaymentSourceResponse
{
    [JsonPropertyName("card")] public CardResponse? Card { get; set; }
}

internal sealed class CardResponse
{
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal sealed class PurchaseUnitResponse
{
    [JsonPropertyName("payments")] public PaymentCollectionResponse? Payments { get; set; }
}

internal sealed class PaymentCollectionResponse
{
    [JsonPropertyName("authorizations")] public List<AuthorizationResponse>? Authorizations { get; set; }
    [JsonPropertyName("captures")] public List<CaptureResponse>? Captures { get; set; }
}

// ---- payments v2: authorization -------------------------------------------------------------
internal sealed class AuthorizationResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public MoneyDto? Amount { get; set; }
    [JsonPropertyName("expiration_time")] public string? ExpirationTime { get; set; }
}

// ---- payments v2: capture -------------------------------------------------------------------
internal sealed class CaptureRequest
{
    [JsonPropertyName("amount")] public MoneyDto? Amount { get; set; }
    [JsonPropertyName("final_capture")] public bool? FinalCapture { get; set; }
}

internal sealed class CaptureResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public MoneyDto? Amount { get; set; }
    [JsonPropertyName("seller_receivable_breakdown")] public SellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal sealed class SellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")] public MoneyDto? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public MoneyDto? PayPalFee { get; set; }
    [JsonPropertyName("net_amount")] public MoneyDto? NetAmount { get; set; }
}

// ---- payments v2: reauthorize ---------------------------------------------------------------
internal sealed class ReauthorizeRequest
{
    [JsonPropertyName("amount")] public MoneyDto Amount { get; set; } = new();
}

// ---- payments v2: refund --------------------------------------------------------------------
internal sealed class RefundRequest
{
    [JsonPropertyName("amount")] public MoneyDto? Amount { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
}

internal sealed class RefundResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public MoneyDto? Amount { get; set; }
}

// ---- vault v3: payment token ----------------------------------------------------------------
internal sealed class VaultPaymentTokenRequest
{
    [JsonPropertyName("payment_source")] public VaultPaymentSource PaymentSource { get; set; } = new();
    [JsonPropertyName("customer")] public VaultCustomer? Customer { get; set; }
}

internal sealed class VaultPaymentSource
{
    [JsonPropertyName("card")] public CardRequest? Card { get; set; }
}

internal sealed class VaultCustomer
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}

internal sealed class VaultPaymentTokenResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("customer")] public VaultCustomer? Customer { get; set; }
    [JsonPropertyName("payment_source")] public PaymentSourceResponse? PaymentSource { get; set; }
}

// ---- transaction search v1 ------------------------------------------------------------------
internal sealed class TransactionSearchResponse
{
    [JsonPropertyName("transaction_details")] public List<TransactionDetail>? TransactionDetails { get; set; }
    [JsonPropertyName("page")] public int? Page { get; set; }
    [JsonPropertyName("total_pages")] public int? TotalPages { get; set; }
    [JsonPropertyName("total_items")] public int? TotalItems { get; set; }
}

internal sealed class TransactionDetail
{
    [JsonPropertyName("transaction_info")] public TransactionInfo? TransactionInfo { get; set; }
}

internal sealed class TransactionInfo
{
    [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
    [JsonPropertyName("paypal_reference_id")] public string? PayPalReferenceId { get; set; }
    [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
    [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
    [JsonPropertyName("transaction_initiation_date")] public string? TransactionInitiationDate { get; set; }
    [JsonPropertyName("transaction_amount")] public MoneyDto? TransactionAmount { get; set; }
    [JsonPropertyName("fee_amount")] public MoneyDto? FeeAmount { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_field")] public string? CustomField { get; set; }
}

// ---- error model ----------------------------------------------------------------------------
internal sealed class PayPalErrorResponse
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
    [JsonPropertyName("details")] public List<PayPalErrorDetail>? Details { get; set; }
}

internal sealed class PayPalErrorDetail
{
    [JsonPropertyName("issue")] public string? Issue { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("field")] public string? Field { get; set; }
}
