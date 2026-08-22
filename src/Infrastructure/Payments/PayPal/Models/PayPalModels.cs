using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal.Models;

internal sealed class PayPalMoney
{
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

internal sealed class PayPalAddress
{
    [JsonPropertyName("address_line_1")]
    public string? AddressLine1 { get; set; }

    [JsonPropertyName("address_line_2")]
    public string? AddressLine2 { get; set; }

    [JsonPropertyName("admin_area_2")]
    public string? AdminArea2 { get; set; }

    [JsonPropertyName("admin_area_1")]
    public string? AdminArea1 { get; set; }

    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }
}

internal sealed class PayPalCardRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("billing_address")]
    public PayPalAddress? BillingAddress { get; set; }

    [JsonPropertyName("vault_id")]
    public string? VaultId { get; set; }

    [JsonPropertyName("stored_credential")]
    public PayPalStoredCredential? StoredCredential { get; set; }
}

internal sealed class PayPalStoredCredential
{
    [JsonPropertyName("payment_initiator")]
    public string? PaymentInitiator { get; set; }

    [JsonPropertyName("payment_type")]
    public string? PaymentType { get; set; }

    [JsonPropertyName("usage")]
    public string? Usage { get; set; }
}

internal sealed class PayPalPaymentSource
{
    [JsonPropertyName("card")]
    public PayPalCardRequest? Card { get; set; }
}

internal sealed class PayPalAmountWithBreakdown
{
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

internal sealed class PayPalPurchaseUnitRequest
{
    [JsonPropertyName("amount")]
    public PayPalAmountWithBreakdown? Amount { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }
}

internal sealed class PayPalOrderRequest
{
    [JsonPropertyName("intent")]
    public string? Intent { get; set; }

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnitRequest>? PurchaseUnits { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSource? PaymentSource { get; set; }
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

internal sealed class PayPalSellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")]
    public PayPalMoney? GrossAmount { get; set; }

    [JsonPropertyName("paypal_fee")]
    public PayPalMoney? PaypalFee { get; set; }

    [JsonPropertyName("net_amount")]
    public PayPalMoney? NetAmount { get; set; }
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

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }
}

internal sealed class PayPalCapture
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoney? Amount { get; set; }

    [JsonPropertyName("seller_receivable_breakdown")]
    public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("final_capture")]
    public bool? FinalCapture { get; set; }
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

internal sealed class PayPalPaymentCollection
{
    [JsonPropertyName("authorizations")]
    public List<PayPalAuthorization>? Authorizations { get; set; }

    [JsonPropertyName("captures")]
    public List<PayPalCapture>? Captures { get; set; }

    [JsonPropertyName("refunds")]
    public List<PayPalRefund>? Refunds { get; set; }
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
    public PayPalAmountWithBreakdown? Amount { get; set; }

    [JsonPropertyName("payments")]
    public PayPalPaymentCollection? Payments { get; set; }
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
    public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }

    [JsonPropertyName("links")]
    public List<PayPalLink>? Links { get; set; }
}

internal sealed class PayPalCaptureRequest
{
    [JsonPropertyName("amount")]
    public PayPalMoney? Amount { get; set; }

    [JsonPropertyName("final_capture")]
    public bool? FinalCapture { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }
}

internal sealed class PayPalReauthorizeRequest
{
    [JsonPropertyName("amount")]
    public PayPalMoney? Amount { get; set; }
}

internal sealed class PayPalRefundRequest
{
    [JsonPropertyName("amount")]
    public PayPalMoney? Amount { get; set; }
}

internal sealed class PayPalCustomer
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("merchant_customer_id")]
    public string? MerchantCustomerId { get; set; }
}

internal sealed class PayPalPaymentTokenRequest
{
    [JsonPropertyName("customer")]
    public PayPalCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSource? PaymentSource { get; set; }
}

internal sealed class PayPalCardResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }
}

internal sealed class PayPalPaymentSourceResponse
{
    [JsonPropertyName("card")]
    public PayPalCardResponse? Card { get; set; }
}

internal sealed class PayPalPaymentTokenResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("customer")]
    public PayPalCustomer? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceResponse? PaymentSource { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("links")]
    public List<PayPalLink>? Links { get; set; }
}

internal sealed class PayPalTransactionInfo
{
    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("paypal_reference_id")]
    public string? PaypalReferenceId { get; set; }

    [JsonPropertyName("transaction_event_code")]
    public string? TransactionEventCode { get; set; }

    [JsonPropertyName("transaction_initiation_date")]
    public string? TransactionInitiationDate { get; set; }

    [JsonPropertyName("transaction_status")]
    public string? TransactionStatus { get; set; }

    [JsonPropertyName("transaction_amount")]
    public PayPalMoney? TransactionAmount { get; set; }

    [JsonPropertyName("fee_amount")]
    public PayPalMoney? FeeAmount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_field")]
    public string? CustomField { get; set; }
}

internal sealed class PayPalTransactionDetail
{
    [JsonPropertyName("transaction_info")]
    public PayPalTransactionInfo? TransactionInfo { get; set; }
}

internal sealed class PayPalSearchResponse
{
    [JsonPropertyName("transaction_details")]
    public List<PayPalTransactionDetail>? TransactionDetails { get; set; }

    [JsonPropertyName("last_refreshed_datetime")]
    public string? LastRefreshedDatetime { get; set; }

    [JsonPropertyName("page")]
    public int? Page { get; set; }

    [JsonPropertyName("total_items")]
    public int? TotalItems { get; set; }

    [JsonPropertyName("total_pages")]
    public int? TotalPages { get; set; }

    [JsonPropertyName("links")]
    public List<PayPalLink>? Links { get; set; }
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

internal sealed class PayPalError
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

internal sealed class PayPalAccessTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}
