using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// --- Auth ---

public class PayPalTokenResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("token_type")] public string TokenType { get; set; } = "";
}

// --- Orders ---

public class PayPalCreateOrderRequest
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "AUTHORIZE";
    [JsonPropertyName("purchase_units")] public List<PayPalPurchaseUnit> PurchaseUnits { get; set; } = new();
    [JsonPropertyName("payment_source")] public PayPalOrderPaymentSource? PaymentSource { get; set; }
}

public class PayPalPurchaseUnit
{
    [JsonPropertyName("amount")] public PayPalAmount Amount { get; set; } = new();
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
}

public class PayPalAmount
{
    [JsonPropertyName("currency_code")] public string CurrencyCode { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}

public class PayPalOrderPaymentSource
{
    [JsonPropertyName("card")] public PayPalCardSource? Card { get; set; }
}

public class PayPalCardSource
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("billing_address")] public PayPalAddress? BillingAddress { get; set; }
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
    [JsonPropertyName("stored_credential")] public PayPalStoredCredential? StoredCredential { get; set; }
}

public class PayPalStoredCredential
{
    [JsonPropertyName("payment_initiator")] public string PaymentInitiator { get; set; } = "CUSTOMER";
    [JsonPropertyName("payment_type")] public string PaymentType { get; set; } = "UNSCHEDULED";
    [JsonPropertyName("usage")] public string Usage { get; set; } = "SUBSEQUENT";
}

public class PayPalAddress
{
    [JsonPropertyName("country_code")] public string CountryCode { get; set; } = "US";
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? City { get; set; }
    [JsonPropertyName("admin_area_1")] public string? State { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
}

public class PayPalOrderResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("purchase_units")] public List<PayPalPurchaseUnitResponse> PurchaseUnits { get; set; } = new();
    [JsonPropertyName("links")] public List<PayPalLink> Links { get; set; } = new();
}

public class PayPalPurchaseUnitResponse
{
    [JsonPropertyName("payments")] public PayPalPayments? Payments { get; set; }
}

public class PayPalPayments
{
    [JsonPropertyName("authorizations")] public List<PayPalAuthorizationResponse> Authorizations { get; set; } = new();
}

public class PayPalAuthorizationResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("amount")] public PayPalAmount? Amount { get; set; }
    [JsonPropertyName("links")] public List<PayPalLink> Links { get; set; } = new();
}

public class PayPalLink
{
    [JsonPropertyName("href")] public string Href { get; set; } = "";
    [JsonPropertyName("rel")] public string Rel { get; set; } = "";
    [JsonPropertyName("method")] public string Method { get; set; } = "";
}

// --- Capture ---

public class PayPalCaptureRequest
{
    [JsonPropertyName("final_capture")] public bool FinalCapture { get; set; } = true;
}

public class PayPalCaptureResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("amount")] public PayPalAmount? Amount { get; set; }
    [JsonPropertyName("seller_receivable_breakdown")] public PayPalSellerBreakdown? SellerBreakdown { get; set; }
}

public class PayPalSellerBreakdown
{
    [JsonPropertyName("gross_amount")] public PayPalAmount? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public PayPalAmount? PayPalFee { get; set; }
    [JsonPropertyName("net_amount")] public PayPalAmount? NetAmount { get; set; }
}

// --- Void ---

public class PayPalVoidResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
}

// --- Reauthorize ---

public class PayPalReauthorizeRequest
{
    [JsonPropertyName("amount")] public PayPalAmount Amount { get; set; } = new();
}

public class PayPalReauthorizeResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
}

// --- Refund ---

public class PayPalRefundRequest
{
    [JsonPropertyName("amount")] public PayPalAmount? Amount { get; set; }
    [JsonPropertyName("note_to_payer")] public string? NoteToPayer { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
}

public class PayPalRefundResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("amount")] public PayPalAmount? Amount { get; set; }
}

// --- Vault ---

public class PayPalCreatePaymentTokenRequest
{
    [JsonPropertyName("payment_source")] public PayPalVaultPaymentSource PaymentSource { get; set; } = new();
    [JsonPropertyName("customer")] public PayPalVaultCustomer? Customer { get; set; }
}

public class PayPalVaultPaymentSource
{
    [JsonPropertyName("card")] public PayPalVaultCard? Card { get; set; }
}

public class PayPalVaultCard
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("billing_address")] public PayPalAddress? BillingAddress { get; set; }
}

public class PayPalVaultCustomer
{
    [JsonPropertyName("merchant_customer_id")] public string? MerchantCustomerId { get; set; }
}

public class PayPalCreatePaymentTokenResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("payment_source")] public PayPalVaultTokenPaymentSource? PaymentSource { get; set; }
    [JsonPropertyName("links")] public List<PayPalLink> Links { get; set; } = new();
}

public class PayPalVaultTokenPaymentSource
{
    [JsonPropertyName("card")] public PayPalVaultCardResponse? Card { get; set; }
}

public class PayPalVaultCardResponse
{
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

// --- Transaction Search ---

public class PayPalTransactionSearchResponse
{
    [JsonPropertyName("transaction_details")] public List<PayPalTransactionDetail> TransactionDetails { get; set; } = new();
    [JsonPropertyName("total_items")] public int TotalItems { get; set; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
}

public class PayPalTransactionDetail
{
    [JsonPropertyName("transaction_info")] public PayPalTransactionInfo? TransactionInfo { get; set; }
    [JsonPropertyName("payer_info")] public PayPalPayerInfo? PayerInfo { get; set; }
}

public class PayPalTransactionInfo
{
    [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
    [JsonPropertyName("paypal_reference_id")] public string? PayPalReferenceId { get; set; }
    [JsonPropertyName("paypal_reference_id_type")] public string? PayPalReferenceIdType { get; set; }
    [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
    [JsonPropertyName("transaction_initiation_date")] public string? TransactionInitiationDate { get; set; }
    [JsonPropertyName("transaction_updated_date")] public string? TransactionUpdatedDate { get; set; }
    [JsonPropertyName("transaction_amount")] public PayPalAmount? TransactionAmount { get; set; }
    [JsonPropertyName("fee_amount")] public PayPalAmount? FeeAmount { get; set; }
    [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
    [JsonPropertyName("custom_field")] public string? CustomField { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("instrument_type")] public string? InstrumentType { get; set; }
    [JsonPropertyName("instrument_sub_type")] public string? InstrumentSubType { get; set; }
}

public class PayPalPayerInfo
{
    [JsonPropertyName("email_address")] public string? EmailAddress { get; set; }
    [JsonPropertyName("payer_name")] public PayPalPayerName? PayerName { get; set; }
}

public class PayPalPayerName
{
    [JsonPropertyName("given_name")] public string? GivenName { get; set; }
    [JsonPropertyName("surname")] public string? Surname { get; set; }
}

// --- Error ---

public class PayPalErrorResponse
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
    [JsonPropertyName("details")] public List<PayPalErrorDetail> Details { get; set; } = new();
}

public class PayPalErrorDetail
{
    [JsonPropertyName("issue")] public string? Issue { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}
