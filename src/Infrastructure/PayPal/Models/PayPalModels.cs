using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Models;

// These POCOs mirror only the fields of PayPal's OpenAPI schemas that this integration reads or
// writes. Property names are pinned with [JsonPropertyName] so they match the spec exactly.

// ---------- Common ----------

public class Money
{
    [JsonPropertyName("currency_code")] public string? CurrencyCode { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
}

public class Link
{
    [JsonPropertyName("href")] public string? Href { get; set; }
    [JsonPropertyName("rel")] public string? Rel { get; set; }
    [JsonPropertyName("method")] public string? Method { get; set; }
}

public class BillingAddress
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
}

// ---------- Checkout Orders v2: create order (authorize) ----------

public class CreateOrderRequest
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "AUTHORIZE";
    [JsonPropertyName("purchase_units")] public List<PurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    [JsonPropertyName("payment_source")] public PaymentSourceRequest? PaymentSource { get; set; }
}

public class PurchaseUnitRequest
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
    [JsonPropertyName("amount")] public Money Amount { get; set; } = new();
}

public class PaymentSourceRequest
{
    [JsonPropertyName("card")] public CardRequest? Card { get; set; }
}

public class CardRequest
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
    [JsonPropertyName("billing_address")] public BillingAddress? BillingAddress { get; set; }
    [JsonPropertyName("attributes")] public CardAttributes? Attributes { get; set; }
}

public class CardAttributes
{
    [JsonPropertyName("verification")] public CardVerification? Verification { get; set; }
}

public class CardVerification
{
    [JsonPropertyName("method")] public string? Method { get; set; }
}

// ---------- Checkout Orders v2: order response ----------

public class OrderResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("payment_source")] public PaymentSourceResponse? PaymentSource { get; set; }
    [JsonPropertyName("purchase_units")] public List<PurchaseUnitResponse>? PurchaseUnits { get; set; }
    [JsonPropertyName("links")] public List<Link>? Links { get; set; }
}

public class PaymentSourceResponse
{
    [JsonPropertyName("card")] public CardResponse? Card { get; set; }
}

public class CardResponse
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
}

public class PurchaseUnitResponse
{
    [JsonPropertyName("payments")] public PaymentCollection? Payments { get; set; }
}

public class PaymentCollection
{
    [JsonPropertyName("authorizations")] public List<AuthorizationResponse>? Authorizations { get; set; }
    [JsonPropertyName("captures")] public List<CaptureResponse>? Captures { get; set; }
}

// ---------- Payments v2: authorization / capture / refund ----------

public class AuthorizationResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public Money? Amount { get; set; }
    [JsonPropertyName("expiration_time")] public string? ExpirationTime { get; set; }
    [JsonPropertyName("links")] public List<Link>? Links { get; set; }
}

public class CaptureResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public Money? Amount { get; set; }
    [JsonPropertyName("final_capture")] public bool? FinalCapture { get; set; }
    [JsonPropertyName("seller_receivable_breakdown")] public SellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

public class SellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")] public Money? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public Money? PaypalFee { get; set; }
    [JsonPropertyName("net_amount")] public Money? NetAmount { get; set; }
}

public class CaptureRequest
{
    [JsonPropertyName("amount")] public Money? Amount { get; set; }
    [JsonPropertyName("final_capture")] public bool? FinalCapture { get; set; }
}

public class ReauthorizeRequest
{
    [JsonPropertyName("amount")] public Money? Amount { get; set; }
}

public class RefundRequest
{
    [JsonPropertyName("amount")] public Money? Amount { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
}

public class RefundResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public Money? Amount { get; set; }
}

// ---------- Vault v3: payment tokens ----------

public class PaymentTokenRequest
{
    [JsonPropertyName("customer")] public VaultCustomer? Customer { get; set; }
    [JsonPropertyName("payment_source")] public VaultPaymentSource PaymentSource { get; set; } = new();
}

public class VaultCustomer
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("merchant_customer_id")] public string? MerchantCustomerId { get; set; }
}

public class VaultPaymentSource
{
    [JsonPropertyName("card")] public VaultCard Card { get; set; } = new();
}

public class VaultCard
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("billing_address")] public BillingAddress? BillingAddress { get; set; }
}

public class PaymentTokenResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("customer")] public VaultCustomer? Customer { get; set; }
    [JsonPropertyName("payment_source")] public VaultPaymentSourceResponse? PaymentSource { get; set; }
}

public class VaultPaymentSourceResponse
{
    [JsonPropertyName("card")] public CardResponse? Card { get; set; }
}

// ---------- Transaction Search v1 ----------

public class TransactionSearchResponse
{
    [JsonPropertyName("transaction_details")] public List<TransactionDetail>? TransactionDetails { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("total_items")] public int TotalItems { get; set; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
}

public class TransactionDetail
{
    [JsonPropertyName("transaction_info")] public TransactionInfo? TransactionInfo { get; set; }
}

public class TransactionInfo
{
    [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
    [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
    [JsonPropertyName("transaction_initiation_date")] public string? TransactionInitiationDate { get; set; }
    [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
    [JsonPropertyName("transaction_amount")] public Money? TransactionAmount { get; set; }
    [JsonPropertyName("fee_amount")] public Money? FeeAmount { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_field")] public string? CustomField { get; set; }
}

// ---------- OAuth token + error ----------

public class TokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}

public class PayPalError
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
    [JsonPropertyName("details")] public List<PayPalErrorDetail>? Details { get; set; }

    // OAuth token endpoint uses a different error shape.
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }
}

public class PayPalErrorDetail
{
    [JsonPropertyName("field")] public string? Field { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("issue")] public string? Issue { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}
