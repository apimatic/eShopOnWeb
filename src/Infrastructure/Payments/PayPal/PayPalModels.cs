using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

internal sealed class PayPalTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal sealed class PayPalMoney
{
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

internal sealed class PayPalError
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("debug_id")]
    public string? DebugId { get; set; }

    [JsonPropertyName("details")]
    public PayPalErrorDetail[]? Details { get; set; }
}

internal sealed class PayPalErrorDetail
{
    [JsonPropertyName("issue")]
    public string? Issue { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("field")]
    public string? Field { get; set; }
}

internal sealed class PayPalLink
{
    [JsonPropertyName("href")]
    public string? Href { get; set; }

    [JsonPropertyName("rel")]
    public string? Rel { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }
}

internal sealed class PayPalOrder
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("intent")]
    public string? Intent { get; set; }

    [JsonPropertyName("purchase_units")]
    public PayPalPurchaseUnit[]? PurchaseUnits { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSource? PaymentSource { get; set; }

    [JsonPropertyName("links")]
    public PayPalLink[]? Links { get; set; }
}

internal sealed class PayPalPurchaseUnit
{
    [JsonPropertyName("reference_id")]
    public string? ReferenceId { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoney? Amount { get; set; }

    [JsonPropertyName("payments")]
    public PayPalPaymentCollection? Payments { get; set; }
}

internal sealed class PayPalPaymentCollection
{
    [JsonPropertyName("authorizations")]
    public PayPalAuthorization[]? Authorizations { get; set; }

    [JsonPropertyName("captures")]
    public PayPalCapture[]? Captures { get; set; }

    [JsonPropertyName("refunds")]
    public PayPalRefund[]? Refunds { get; set; }
}

internal sealed class PayPalAuthorization
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoney? Amount { get; set; }

    [JsonPropertyName("expiration_time")]
    public string? ExpirationTime { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }
}

internal sealed class PayPalCapture
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoney? Amount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("final_capture")]
    public bool? FinalCapture { get; set; }

    [JsonPropertyName("seller_receivable_breakdown")]
    public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
}

internal sealed class PayPalSellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")]
    public PayPalMoney? GrossAmount { get; set; }

    [JsonPropertyName("paypal_fee")]
    public PayPalMoney? PaypalFee { get; set; }

    [JsonPropertyName("net_amount")]
    public PayPalMoney? NetAmount { get; set; }

    [JsonPropertyName("receivable_amount")]
    public PayPalMoney? ReceivableAmount { get; set; }
}

internal sealed class PayPalRefund
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoney? Amount { get; set; }
}

internal sealed class PayPalPaymentSource
{
    [JsonPropertyName("card")]
    public PayPalCard? Card { get; set; }
}

internal sealed class PayPalCard
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("authentication_result")]
    public PayPalAuthenticationResult? AuthenticationResult { get; set; }
}

internal sealed class PayPalAuthenticationResult
{
    [JsonPropertyName("liability_shift")]
    public string? LiabilityShift { get; set; }

    [JsonPropertyName("three_d_secure")]
    public PayPalThreeDSecure? ThreeDSecure { get; set; }
}

internal sealed class PayPalThreeDSecure
{
    [JsonPropertyName("enrollment_status")]
    public string? EnrollmentStatus { get; set; }

    [JsonPropertyName("authentication_status")]
    public string? AuthenticationStatus { get; set; }
}

internal sealed class PayPalPaymentTokenResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("customer")]
    public PayPalCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSource? PaymentSource { get; set; }
}

internal sealed class PayPalCustomer
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("merchant_customer_id")]
    public string? MerchantCustomerId { get; set; }
}

internal sealed class PayPalTransactionSearchResponse
{
    [JsonPropertyName("transaction_details")]
    public PayPalTransactionDetail[]? TransactionDetails { get; set; }

    [JsonPropertyName("total_items")]
    public int? TotalItems { get; set; }

    [JsonPropertyName("total_pages")]
    public int? TotalPages { get; set; }

    [JsonPropertyName("page")]
    public int? Page { get; set; }
}

internal sealed class PayPalTransactionDetail
{
    [JsonPropertyName("transaction_info")]
    public PayPalTransactionInfo? TransactionInfo { get; set; }
}

internal sealed class PayPalTransactionInfo
{
    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("paypal_reference_id")]
    public string? PaypalReferenceId { get; set; }

    [JsonPropertyName("paypal_reference_id_type")]
    public string? PaypalReferenceIdType { get; set; }

    [JsonPropertyName("transaction_event_code")]
    public string? TransactionEventCode { get; set; }

    [JsonPropertyName("transaction_initiation_date")]
    public string? TransactionInitiationDate { get; set; }

    [JsonPropertyName("transaction_amount")]
    public PayPalMoney? TransactionAmount { get; set; }

    [JsonPropertyName("fee_amount")]
    public PayPalMoney? FeeAmount { get; set; }

    [JsonPropertyName("transaction_status")]
    public string? TransactionStatus { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_field")]
    public string? CustomField { get; set; }
}
