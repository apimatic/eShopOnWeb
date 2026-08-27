using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

// DTOs mirroring the PayPal OpenAPI specifications in api-specs/paypal
// (checkout_orders_v2, payments_payment_v2, vault_payment_tokens_v3, transaction_search_v1).
// Property names match the spec's JSON field names exactly.

public sealed class PayPalMoney
{
    [JsonPropertyName("currency_code")] public string CurrencyCode { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}

public sealed class PayPalAddress
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string CountryCode { get; set; } = "";
}

public sealed class PayPalCardRequest
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("billing_address")] public PayPalAddress? BillingAddress { get; set; }
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
}

public sealed class PayPalPaymentSource
{
    [JsonPropertyName("card")] public PayPalCardRequest? Card { get; set; }
}

public sealed class PayPalPurchaseUnitRequest
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney Amount { get; set; } = new();
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}

// checkout_orders_v2: order_request
public sealed class PayPalOrderRequest
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "AUTHORIZE";
    [JsonPropertyName("purchase_units")] public List<PayPalPurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    [JsonPropertyName("payment_source")] public PayPalPaymentSource? PaymentSource { get; set; }
}

public sealed class PayPalLinkDescription
{
    [JsonPropertyName("href")] public string? Href { get; set; }
    [JsonPropertyName("rel")] public string? Rel { get; set; }
    [JsonPropertyName("method")] public string? Method { get; set; }
}

public sealed class PayPalAuthorization
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("expiration_time")] public string? ExpirationTime { get; set; }
}

public sealed class PayPalSellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")] public PayPalMoney? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public PayPalMoney? PayPalFee { get; set; }
    [JsonPropertyName("net_amount")] public PayPalMoney? NetAmount { get; set; }
}

public sealed class PayPalCapture
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("final_capture")] public bool? FinalCapture { get; set; }
    [JsonPropertyName("seller_receivable_breakdown")] public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

public sealed class PayPalRefund
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
}

public sealed class PayPalPaymentCollection
{
    [JsonPropertyName("authorizations")] public List<PayPalAuthorization>? Authorizations { get; set; }
    [JsonPropertyName("captures")] public List<PayPalCapture>? Captures { get; set; }
    [JsonPropertyName("refunds")] public List<PayPalRefund>? Refunds { get; set; }
}

public sealed class PayPalPurchaseUnit
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("payments")] public PayPalPaymentCollection? Payments { get; set; }
}

// checkout_orders_v2: order / order_authorize_response
public sealed class PayPalOrderResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("purchase_units")] public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }
    [JsonPropertyName("links")] public List<PayPalLinkDescription>? Links { get; set; }
}

// payments_payment_v2: capture_request
public sealed class PayPalCaptureRequest
{
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("final_capture")] public bool FinalCapture { get; set; }
    [JsonPropertyName("note_to_payer")] public string? NoteToPayer { get; set; }
}

// payments_payment_v2: reauthorize_request
public sealed class PayPalReauthorizeRequest
{
    [JsonPropertyName("amount")] public PayPalMoney Amount { get; set; } = new();
}

// payments_payment_v2: refund_request
public sealed class PayPalRefundRequest
{
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("note_to_payer")] public string? NoteToPayer { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
}

// vault_payment_tokens_v3: payment_token_request
public sealed class PayPalPaymentTokenRequest
{
    public sealed class PayPalCustomer
    {
        [JsonPropertyName("merchant_customer_id")] public string? MerchantCustomerId { get; set; }
    }

    public sealed class PayPalTokenPaymentSource
    {
        [JsonPropertyName("card")] public PayPalCardRequest? Card { get; set; }
    }

    [JsonPropertyName("customer")] public PayPalCustomer? Customer { get; set; }
    [JsonPropertyName("payment_source")] public PayPalTokenPaymentSource PaymentSource { get; set; } = new();
}

// vault_payment_tokens_v3: payment_token_response
public sealed class PayPalPaymentTokenResponse
{
    public sealed class PayPalCardResponse
    {
        [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
        [JsonPropertyName("brand")] public string? Brand { get; set; }
        [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    }

    public sealed class PayPalTokenResponsePaymentSource
    {
        [JsonPropertyName("card")] public PayPalCardResponse? Card { get; set; }
    }

    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("payment_source")] public PayPalTokenResponsePaymentSource? PaymentSource { get; set; }
}

// transaction_search_v1: search_response / transaction_detail / transaction_info
public sealed class PayPalTransactionInfo
{
    [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
    [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
    [JsonPropertyName("transaction_initiation_date")] public string? TransactionInitiationDate { get; set; }
    [JsonPropertyName("transaction_amount")] public PayPalMoney? TransactionAmount { get; set; }
    [JsonPropertyName("fee_amount")] public PayPalMoney? FeeAmount { get; set; }
    [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_field")] public string? CustomField { get; set; }
    [JsonPropertyName("paypal_reference_id")] public string? PayPalReferenceId { get; set; }
    [JsonPropertyName("paypal_reference_id_type")] public string? PayPalReferenceIdType { get; set; }
}

public sealed class PayPalTransactionDetail
{
    [JsonPropertyName("transaction_info")] public PayPalTransactionInfo? TransactionInfo { get; set; }
}

public sealed class PayPalTransactionSearchResponse
{
    [JsonPropertyName("transaction_details")] public List<PayPalTransactionDetail>? TransactionDetails { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("total_items")] public int TotalItems { get; set; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
}

// OAuth2 client credentials token response (tokenUrl /v1/oauth2/token per the spec's security scheme)
public sealed class PayPalTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}

// PayPal error model (components/schemas/error)
public sealed class PayPalError
{
    public sealed class PayPalErrorDetails
    {
        [JsonPropertyName("issue")] public string? Issue { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
    }

    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
    [JsonPropertyName("details")] public List<PayPalErrorDetails>? Details { get; set; }
}
