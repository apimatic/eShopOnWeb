using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services;

// DTOs for the PayPal REST APIs (Orders v2, Payments v2, Vault v3,
// Transaction Search v1). Only the fields the integration reads are mapped.

internal class PayPalTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}

internal class PayPalMoney
{
    [JsonPropertyName("currency_code")] public string? CurrencyCode { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
}

internal class PayPalOrderResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("purchase_units")] public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }
}

internal class PayPalPurchaseUnit
{
    [JsonPropertyName("payments")] public PayPalPaymentCollection? Payments { get; set; }
}

internal class PayPalPaymentCollection
{
    [JsonPropertyName("authorizations")] public List<PayPalAuthorization>? Authorizations { get; set; }
    [JsonPropertyName("captures")] public List<PayPalCapture>? Captures { get; set; }
}

internal class PayPalAuthorization
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("create_time")] public string? CreateTime { get; set; }
    [JsonPropertyName("expiration_time")] public string? ExpirationTime { get; set; }
}

internal class PayPalCapture
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("seller_receivable_breakdown")] public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal class PayPalSellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")] public PayPalMoney? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public PayPalMoney? PayPalFee { get; set; }
    [JsonPropertyName("net_amount")] public PayPalMoney? NetAmount { get; set; }
}

internal class PayPalRefundResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
}

internal class PayPalSetupTokenResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("customer")] public PayPalCustomer? Customer { get; set; }
}

internal class PayPalCustomer
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}

internal class PayPalPaymentTokenResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("customer")] public PayPalCustomer? Customer { get; set; }
    [JsonPropertyName("payment_source")] public PayPalVaultPaymentSource? PaymentSource { get; set; }
}

internal class PayPalVaultPaymentSource
{
    [JsonPropertyName("card")] public PayPalVaultedCardDetails? Card { get; set; }
}

internal class PayPalVaultedCardDetails
{
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
}

internal class PayPalErrorResponse
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
    [JsonPropertyName("details")] public List<PayPalErrorDetail>? Details { get; set; }
}

internal class PayPalErrorDetail
{
    [JsonPropertyName("issue")] public string? Issue { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}

internal class PayPalTransactionSearchResponse
{
    [JsonPropertyName("transaction_details")] public List<PayPalTransactionDetail>? TransactionDetails { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
}

internal class PayPalTransactionDetail
{
    [JsonPropertyName("transaction_info")] public PayPalTransactionInfo? TransactionInfo { get; set; }
}

internal class PayPalTransactionInfo
{
    [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
    [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
    [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
    [JsonPropertyName("transaction_amount")] public PayPalMoney? TransactionAmount { get; set; }
    [JsonPropertyName("transaction_initiation_date")] public string? TransactionInitiationDate { get; set; }
}
