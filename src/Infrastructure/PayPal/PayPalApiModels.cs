using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

internal sealed class PayPalTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal sealed class PayPalErrorResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("debug_id")]
    public string? DebugId { get; set; }

    [JsonPropertyName("details")]
    public List<PayPalErrorDetail>? Details { get; set; }
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

internal sealed class PayPalOrderResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceResponse? PaymentSource { get; set; }

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }

    [JsonPropertyName("links")]
    public List<PayPalLink>? Links { get; set; }
}

internal sealed class PayPalPaymentSourceResponse
{
    [JsonPropertyName("card")]
    public PayPalCardResponse? Card { get; set; }
}

internal sealed class PayPalCardResponse
{
    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class PayPalPurchaseUnit
{
    [JsonPropertyName("payments")]
    public PayPalPayments? Payments { get; set; }
}

internal sealed class PayPalPayments
{
    [JsonPropertyName("authorizations")]
    public List<PayPalAuthorizationResource>? Authorizations { get; set; }

    [JsonPropertyName("captures")]
    public List<PayPalCaptureResource>? Captures { get; set; }
}

internal sealed class PayPalAuthorizationResource
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalAmount? Amount { get; set; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; set; }

    [JsonPropertyName("expiration_time")]
    public string? ExpirationTime { get; set; }

    [JsonPropertyName("update_time")]
    public string? UpdateTime { get; set; }
}

internal sealed class PayPalCaptureResource
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalAmount? Amount { get; set; }

    [JsonPropertyName("seller_receivable_breakdown")]
    public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; set; }
}

internal sealed class PayPalSellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")]
    public PayPalAmount? GrossAmount { get; set; }

    [JsonPropertyName("paypal_fee")]
    public PayPalAmount? PaypalFee { get; set; }

    [JsonPropertyName("net_amount")]
    public PayPalAmount? NetAmount { get; set; }
}

internal sealed class PayPalAmount
{
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

internal sealed class PayPalRefundResource
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalAmount? Amount { get; set; }
}

internal sealed class PayPalLink
{
    [JsonPropertyName("href")]
    public string? Href { get; set; }

    [JsonPropertyName("rel")]
    public string? Rel { get; set; }
}

internal sealed class PayPalSetupTokenResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("customer")]
    public PayPalCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceResponse? PaymentSource { get; set; }

    [JsonPropertyName("links")]
    public List<PayPalLink>? Links { get; set; }
}

internal sealed class PayPalPaymentTokenResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("customer")]
    public PayPalCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceResponse? PaymentSource { get; set; }
}

internal sealed class PayPalCustomer
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

internal sealed class PayPalTransactionSearchResponse
{
    [JsonPropertyName("transaction_details")]
    public List<PayPalTransactionDetail>? TransactionDetails { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }
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

    [JsonPropertyName("transaction_event_code")]
    public string? TransactionEventCode { get; set; }

    [JsonPropertyName("transaction_status")]
    public string? TransactionStatus { get; set; }

    [JsonPropertyName("transaction_initiation_date")]
    public string? TransactionInitiationDate { get; set; }

    [JsonPropertyName("transaction_amount")]
    public PayPalAmount? TransactionAmount { get; set; }

    [JsonPropertyName("fee_amount")]
    public PayPalAmount? FeeAmount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_field")]
    public string? CustomField { get; set; }
}
