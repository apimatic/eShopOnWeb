using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

internal sealed class PayPalMoneyJson
{
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

internal sealed class PayPalLinkJson
{
    [JsonPropertyName("href")]
    public string? Href { get; set; }

    [JsonPropertyName("rel")]
    public string? Rel { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }
}

internal sealed class PayPalErrorDetailJson
{
    [JsonPropertyName("issue")]
    public string? Issue { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("field")]
    public string? Field { get; set; }
}

internal sealed class PayPalErrorJson
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("debug_id")]
    public string? DebugId { get; set; }

    [JsonPropertyName("details")]
    public List<PayPalErrorDetailJson>? Details { get; set; }
}

internal sealed class PayPalTokenJson
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }
}

internal sealed class PayPalAddressJson
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

internal sealed class PayPalCardJson
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("vault_id")]
    public string? VaultId { get; set; }

    [JsonPropertyName("billing_address")]
    public PayPalAddressJson? BillingAddress { get; set; }

    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("authentication_result")]
    public PayPalAuthenticationResultJson? AuthenticationResult { get; set; }
}

internal sealed class PayPalAuthenticationResultJson
{
    [JsonPropertyName("three_d_secure")]
    public PayPalThreeDSecureJson? ThreeDSecure { get; set; }
}

internal sealed class PayPalThreeDSecureJson
{
    [JsonPropertyName("authentication_status")]
    public string? AuthenticationStatus { get; set; }

    [JsonPropertyName("enrollment_status")]
    public string? EnrollmentStatus { get; set; }
}

internal sealed class PayPalPaymentSourceJson
{
    [JsonPropertyName("card")]
    public PayPalCardJson? Card { get; set; }
}

internal sealed class PayPalCustomerJson
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("merchant_customer_id")]
    public string? MerchantCustomerId { get; set; }
}

internal sealed class PayPalRelatedIdsJson
{
    [JsonPropertyName("order_id")]
    public string? OrderId { get; set; }

    [JsonPropertyName("authorization_id")]
    public string? AuthorizationId { get; set; }

    [JsonPropertyName("capture_id")]
    public string? CaptureId { get; set; }
}

internal sealed class PayPalSupplementaryDataJson
{
    [JsonPropertyName("related_ids")]
    public PayPalRelatedIdsJson? RelatedIds { get; set; }
}

internal sealed class PayPalAuthorizationJson
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoneyJson? Amount { get; set; }

    [JsonPropertyName("expiration_time")]
    public string? ExpirationTime { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("supplementary_data")]
    public PayPalSupplementaryDataJson? SupplementaryData { get; set; }
}

internal sealed class PayPalSellerReceivableJson
{
    [JsonPropertyName("gross_amount")]
    public PayPalMoneyJson? GrossAmount { get; set; }

    [JsonPropertyName("paypal_fee")]
    public PayPalMoneyJson? PaypalFee { get; set; }

    [JsonPropertyName("net_amount")]
    public PayPalMoneyJson? NetAmount { get; set; }
}

internal sealed class PayPalCaptureJson
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoneyJson? Amount { get; set; }

    [JsonPropertyName("seller_receivable_breakdown")]
    public PayPalSellerReceivableJson? SellerReceivableBreakdown { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }
}

internal sealed class PayPalRefundJson
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoneyJson? Amount { get; set; }
}

internal sealed class PayPalPaymentsJson
{
    [JsonPropertyName("authorizations")]
    public List<PayPalAuthorizationJson>? Authorizations { get; set; }

    [JsonPropertyName("captures")]
    public List<PayPalCaptureJson>? Captures { get; set; }
}

internal sealed class PayPalPurchaseUnitJson
{
    [JsonPropertyName("reference_id")]
    public string? ReferenceId { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoneyJson? Amount { get; set; }

    [JsonPropertyName("payments")]
    public PayPalPaymentsJson? Payments { get; set; }
}

internal sealed class PayPalOrderJson
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("intent")]
    public string? Intent { get; set; }

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnitJson>? PurchaseUnits { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceJson? PaymentSource { get; set; }

    [JsonPropertyName("links")]
    public List<PayPalLinkJson>? Links { get; set; }
}

internal sealed class PayPalVaultResponseJson
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("customer")]
    public PayPalCustomerJson? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceJson? PaymentSource { get; set; }

    [JsonPropertyName("links")]
    public List<PayPalLinkJson>? Links { get; set; }
}

internal sealed class PayPalTransactionAmountJson
{
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

internal sealed class PayPalTransactionInfoJson
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
    public PayPalTransactionAmountJson? TransactionAmount { get; set; }

    [JsonPropertyName("fee_amount")]
    public PayPalTransactionAmountJson? FeeAmount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_field")]
    public string? CustomField { get; set; }
}

internal sealed class PayPalTransactionDetailJson
{
    [JsonPropertyName("transaction_info")]
    public PayPalTransactionInfoJson? TransactionInfo { get; set; }
}

internal sealed class PayPalTransactionSearchJson
{
    [JsonPropertyName("transaction_details")]
    public List<PayPalTransactionDetailJson>? TransactionDetails { get; set; }

    [JsonPropertyName("total_pages")]
    public int? TotalPages { get; set; }

    [JsonPropertyName("total_items")]
    public int? TotalItems { get; set; }
}
