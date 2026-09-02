using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

// Wire DTOs for the PayPal REST API. Property names are mapped explicitly; these types
// must never carry card data into logs.

internal class PayPalTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}

internal class PayPalMoney
{
    [JsonPropertyName("currency_code")] public string? CurrencyCode { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
}

internal class PayPalAddress
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? City { get; set; }
    [JsonPropertyName("admin_area_1")] public string? State { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
}

internal class PayPalCardRequest
{
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("billing_address")] public PayPalAddress? BillingAddress { get; set; }
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
    [JsonPropertyName("stored_credential")] public PayPalStoredCredential? StoredCredential { get; set; }
}

internal class PayPalStoredCredential
{
    [JsonPropertyName("payment_initiator")] public string? PaymentInitiator { get; set; }
    [JsonPropertyName("payment_type")] public string? PaymentType { get; set; }
}

internal class PayPalPaymentSourceRequest
{
    [JsonPropertyName("card")] public PayPalCardRequest? Card { get; set; }
    [JsonPropertyName("token")] public PayPalTokenReference? Token { get; set; }
}

internal class PayPalTokenReference
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
}

internal class PayPalPurchaseUnitRequest
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
}

internal class PayPalCreateOrderRequest
{
    [JsonPropertyName("intent")] public string? Intent { get; set; }
    [JsonPropertyName("purchase_units")] public List<PayPalPurchaseUnitRequest>? PurchaseUnits { get; set; }
}

internal class PayPalAuthorizeOrderRequest
{
    [JsonPropertyName("payment_source")] public PayPalPaymentSourceRequest? PaymentSource { get; set; }
}

internal class PayPalSellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")] public PayPalMoney? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public PayPalMoney? PayPalFee { get; set; }
    [JsonPropertyName("net_amount")] public PayPalMoney? NetAmount { get; set; }
}

internal class PayPalAuthorization
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("expiration_time")] public string? ExpirationTime { get; set; }
}

internal class PayPalCapture
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("seller_receivable_breakdown")] public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal class PayPalPayments
{
    [JsonPropertyName("authorizations")] public List<PayPalAuthorization>? Authorizations { get; set; }
    [JsonPropertyName("captures")] public List<PayPalCapture>? Captures { get; set; }
}

internal class PayPalPurchaseUnit
{
    [JsonPropertyName("payments")] public PayPalPayments? Payments { get; set; }
}

internal class PayPalOrderResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("purchase_units")] public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }
}

internal class PayPalCaptureRequest
{
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("final_capture")] public bool FinalCapture { get; set; }
}

internal class PayPalReauthorizeRequest
{
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
}

internal class PayPalRefundRequest
{
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("note_to_payer")] public string? NoteToPayer { get; set; }
}

internal class PayPalRefund
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
}

internal class PayPalSetupTokenRequest
{
    [JsonPropertyName("payment_source")] public PayPalPaymentSourceRequest? PaymentSource { get; set; }
}

internal class PayPalSetupTokenResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

internal class PayPalCustomer
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}

internal class PayPalPaymentTokenRequest
{
    [JsonPropertyName("payment_source")] public PayPalPaymentSourceRequest? PaymentSource { get; set; }
    [JsonPropertyName("customer")] public PayPalCustomer? Customer { get; set; }
}

internal class PayPalCardTokenDetails
{
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal class PayPalPaymentTokenSource
{
    [JsonPropertyName("card")] public PayPalCardTokenDetails? Card { get; set; }
}

internal class PayPalPaymentTokenResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("customer")] public PayPalCustomer? Customer { get; set; }
    [JsonPropertyName("payment_source")] public PayPalPaymentTokenSource? PaymentSource { get; set; }
}

internal class PayPalTransactionInfo
{
    [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
    [JsonPropertyName("paypal_reference_id")] public string? PayPalReferenceId { get; set; }
    [JsonPropertyName("paypal_reference_id_type")] public string? PayPalReferenceIdType { get; set; }
    [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
    [JsonPropertyName("transaction_initiation_date")] public string? TransactionInitiationDate { get; set; }
    [JsonPropertyName("transaction_updated_date")] public string? TransactionUpdatedDate { get; set; }
    [JsonPropertyName("transaction_amount")] public PayPalMoney? TransactionAmount { get; set; }
    [JsonPropertyName("fee_amount")] public PayPalMoney? FeeAmount { get; set; }
    [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_field")] public string? CustomField { get; set; }
}

internal class PayPalTransactionDetail
{
    [JsonPropertyName("transaction_info")] public PayPalTransactionInfo? TransactionInfo { get; set; }
}

internal class PayPalTransactionSearchResponse
{
    [JsonPropertyName("transaction_details")] public List<PayPalTransactionDetail>? TransactionDetails { get; set; }
    [JsonPropertyName("total_items")] public int TotalItems { get; set; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
}

internal class PayPalErrorDetail
{
    [JsonPropertyName("issue")] public string? Issue { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}

internal class PayPalErrorResponse
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
    [JsonPropertyName("details")] public List<PayPalErrorDetail>? Details { get; set; }
}
