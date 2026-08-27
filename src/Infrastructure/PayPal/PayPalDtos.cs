using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Wire DTOs for PayPal REST APIs. Property names are mapped explicitly because PayPal's
// snake_case does not line up with .NET naming policies (e.g. "paypal_fee").

internal sealed class PayPalTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
}

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
}

internal sealed class PayPalMoney
{
    [JsonPropertyName("currency_code")] public string? CurrencyCode { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
}

internal sealed class PayPalLink
{
    [JsonPropertyName("href")] public string? Href { get; set; }
    [JsonPropertyName("rel")] public string? Rel { get; set; }
    [JsonPropertyName("method")] public string? Method { get; set; }
}

// ---- Orders v2 ----

internal sealed class PayPalCreateOrderRequest
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "AUTHORIZE";
    [JsonPropertyName("purchase_units")] public List<PayPalPurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    [JsonPropertyName("payment_source")] public PayPalPaymentSourceRequest? PaymentSource { get; set; }
}

internal sealed class PayPalPurchaseUnitRequest
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
}

internal sealed class PayPalAuthorizeOrderRequest
{
    [JsonPropertyName("payment_source")] public PayPalPaymentSourceRequest? PaymentSource { get; set; }
}

internal sealed class PayPalPaymentSourceRequest
{
    [JsonPropertyName("card")] public PayPalCardRequest? Card { get; set; }
}

internal sealed class PayPalCardRequest
{
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("billing_address")] public PayPalAddressRequest? BillingAddress { get; set; }
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
    [JsonPropertyName("stored_credential")] public PayPalStoredCredentialRequest? StoredCredential { get; set; }
    [JsonPropertyName("verification_method")] public string? VerificationMethod { get; set; }
    [JsonPropertyName("experience_context")] public PayPalExperienceContextRequest? ExperienceContext { get; set; }
}

internal sealed class PayPalExperienceContextRequest
{
    [JsonPropertyName("brand_name")] public string? BrandName { get; set; }
    [JsonPropertyName("locale")] public string? Locale { get; set; }
    [JsonPropertyName("return_url")] public string? ReturnUrl { get; set; }
    [JsonPropertyName("cancel_url")] public string? CancelUrl { get; set; }
}

internal sealed class PayPalStoredCredentialRequest
{
    [JsonPropertyName("payment_initiator")] public string? PaymentInitiator { get; set; }
    [JsonPropertyName("payment_type")] public string? PaymentType { get; set; }
    [JsonPropertyName("usage")] public string? Usage { get; set; }
}

internal sealed class PayPalAddressRequest
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
}

internal sealed class PayPalOrderResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("purchase_units")] public List<PayPalPurchaseUnitResponse>? PurchaseUnits { get; set; }
    [JsonPropertyName("links")] public List<PayPalLink>? Links { get; set; }
}

internal sealed class PayPalPurchaseUnitResponse
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("payments")] public PayPalPaymentsResponse? Payments { get; set; }
}

internal sealed class PayPalPaymentsResponse
{
    [JsonPropertyName("authorizations")] public List<PayPalAuthorizationResponse>? Authorizations { get; set; }
    [JsonPropertyName("captures")] public List<PayPalCaptureResponse>? Captures { get; set; }
    [JsonPropertyName("refunds")] public List<PayPalRefundResponse>? Refunds { get; set; }
}

// ---- Payments v2 ----

internal sealed class PayPalAuthorizationResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("expiration_time")] public string? ExpirationTime { get; set; }
    [JsonPropertyName("links")] public List<PayPalLink>? Links { get; set; }
}

internal sealed class PayPalAmountRequest
{
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("final_capture")] public bool? FinalCapture { get; set; }
}

internal sealed class PayPalCaptureResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("seller_receivable_breakdown")] public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal sealed class PayPalSellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")] public PayPalMoney? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public PayPalMoney? PayPalFee { get; set; }
    [JsonPropertyName("net_amount")] public PayPalMoney? NetAmount { get; set; }
}

internal sealed class PayPalRefundResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
}

// ---- Payment Method Tokens v3 ----

internal sealed class PayPalSetupTokenRequest
{
    [JsonPropertyName("payment_source")] public PayPalPaymentSourceRequest? PaymentSource { get; set; }
}

internal sealed class PayPalSetupTokenResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("customer")] public PayPalCustomerResponse? Customer { get; set; }
    [JsonPropertyName("links")] public List<PayPalLink>? Links { get; set; }
}

internal sealed class PayPalCreatePaymentTokenRequest
{
    [JsonPropertyName("payment_source")] public PayPalTokenSourceRequest? PaymentSource { get; set; }
}

internal sealed class PayPalTokenSourceRequest
{
    [JsonPropertyName("token")] public PayPalTokenReference? Token { get; set; }
}

internal sealed class PayPalTokenReference
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
}

internal sealed class PayPalPaymentTokenResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("customer")] public PayPalCustomerResponse? Customer { get; set; }
    [JsonPropertyName("payment_source")] public PayPalPaymentTokenSourceResponse? PaymentSource { get; set; }
}

internal sealed class PayPalCustomerResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}

internal sealed class PayPalPaymentTokenSourceResponse
{
    [JsonPropertyName("card")] public PayPalVaultedCardResponse? Card { get; set; }
}

internal sealed class PayPalVaultedCardResponse
{
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

// ---- Transaction Search v1 ----

internal sealed class PayPalTransactionSearchResponse
{
    [JsonPropertyName("transaction_details")] public List<PayPalTransactionDetail>? TransactionDetails { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("total_items")] public int TotalItems { get; set; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
}

internal sealed class PayPalTransactionDetail
{
    [JsonPropertyName("transaction_info")] public PayPalTransactionInfo? TransactionInfo { get; set; }
}

internal sealed class PayPalTransactionInfo
{
    [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
    [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
    [JsonPropertyName("transaction_initiation_date")] public string? TransactionInitiationDate { get; set; }
    [JsonPropertyName("transaction_updated_date")] public string? TransactionUpdatedDate { get; set; }
    [JsonPropertyName("transaction_amount")] public PayPalMoney? TransactionAmount { get; set; }
    [JsonPropertyName("fee_amount")] public PayPalMoney? FeeAmount { get; set; }
    [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
}
