using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public sealed record PayPalMoney(
    [property: JsonPropertyName("currency_code")] string CurrencyCode,
    [property: JsonPropertyName("value")] string Value);

public sealed record PayPalCreateOrderRequest(
    [property: JsonPropertyName("intent")] string Intent,
    [property: JsonPropertyName("purchase_units")] IReadOnlyList<PayPalPurchaseUnitRequest> PurchaseUnits);

public sealed record PayPalPurchaseUnitRequest(
    [property: JsonPropertyName("reference_id")] string ReferenceId,
    [property: JsonPropertyName("amount")] PayPalMoney Amount,
    [property: JsonPropertyName("custom_id")] string CustomId,
    [property: JsonPropertyName("invoice_id")] string InvoiceId);

public sealed record PayPalAuthorizeRequest(
    [property: JsonPropertyName("payment_source")] PayPalPaymentSource PaymentSource);

public sealed record PayPalPaymentSource(
    [property: JsonPropertyName("card")] PayPalCard Card);

public sealed record PayPalCard
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    [JsonPropertyName("number")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Number { get; init; }

    [JsonPropertyName("expiry")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Expiry { get; init; }

    [JsonPropertyName("security_code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SecurityCode { get; init; }

    [JsonPropertyName("billing_address")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PayPalBillingAddress? BillingAddress { get; init; }

    [JsonPropertyName("vault_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VaultId { get; init; }

    [JsonPropertyName("stored_credential")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PayPalStoredCredential? StoredCredential { get; init; }
}

public sealed record PayPalStoredCredential(
    [property: JsonPropertyName("payment_initiator")] string PaymentInitiator,
    [property: JsonPropertyName("payment_type")] string PaymentType,
    [property: JsonPropertyName("usage")] string Usage);

public sealed record PayPalBillingAddress
{
    [JsonPropertyName("address_line_1")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AddressLine1 { get; init; }
    [JsonPropertyName("address_line_2")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AddressLine2 { get; init; }
    [JsonPropertyName("admin_area_2")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? City { get; init; }
    [JsonPropertyName("admin_area_1")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? State { get; init; }
    [JsonPropertyName("postal_code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PostalCode { get; init; }
    [JsonPropertyName("country_code")]
    public required string CountryCode { get; init; }
}

public sealed record PayPalOrderResponse
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("purchase_units")] public List<PayPalPurchaseUnitResponse> PurchaseUnits { get; init; } = new();
    [JsonPropertyName("links")] public List<PayPalLink> Links { get; init; } = new();
}

public sealed record PayPalPurchaseUnitResponse
{
    [JsonPropertyName("payments")] public PayPalPaymentCollection? Payments { get; init; }
}

public sealed record PayPalPaymentCollection
{
    [JsonPropertyName("authorizations")] public List<PayPalAuthorization> Authorizations { get; init; } = new();
    [JsonPropertyName("captures")] public List<PayPalCapture> Captures { get; init; } = new();
}

public sealed record PayPalAuthorization
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("amount")] public PayPalMoney Amount { get; init; } = new("", "0");
    [JsonPropertyName("create_time")] public DateTimeOffset? CreateTime { get; init; }
    [JsonPropertyName("update_time")] public DateTimeOffset? UpdateTime { get; init; }
    [JsonPropertyName("expiration_time")] public DateTimeOffset? ExpirationTime { get; init; }
}

public sealed record PayPalCapture
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("amount")] public PayPalMoney Amount { get; init; } = new("", "0");
    [JsonPropertyName("seller_receivable_breakdown")] public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; init; }
    [JsonPropertyName("create_time")] public DateTimeOffset? CreateTime { get; init; }
    [JsonPropertyName("update_time")] public DateTimeOffset? UpdateTime { get; init; }
}

public sealed record PayPalSellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")] public PayPalMoney? GrossAmount { get; init; }
    [JsonPropertyName("paypal_fee")] public PayPalMoney? PayPalFee { get; init; }
    [JsonPropertyName("net_amount")] public PayPalMoney? NetAmount { get; init; }
}

public sealed record PayPalRefund
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("amount")] public PayPalMoney Amount { get; init; } = new("", "0");
    [JsonPropertyName("create_time")] public DateTimeOffset? CreateTime { get; init; }
    [JsonPropertyName("update_time")] public DateTimeOffset? UpdateTime { get; init; }
}

public sealed record PayPalCaptureRequest(
    [property: JsonPropertyName("amount")] PayPalMoney Amount,
    [property: JsonPropertyName("invoice_id")] string InvoiceId,
    [property: JsonPropertyName("final_capture")] bool FinalCapture);

public sealed record PayPalReauthorizeRequest(
    [property: JsonPropertyName("amount")] PayPalMoney Amount);

public sealed record PayPalRefundRequest(
    [property: JsonPropertyName("amount")] PayPalMoney Amount,
    [property: JsonPropertyName("custom_id")] string CustomId,
    [property: JsonPropertyName("note_to_payer")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NoteToPayer);

public sealed record PayPalPaymentTokenRequest(
    [property: JsonPropertyName("customer")] PayPalCustomerRequest Customer,
    [property: JsonPropertyName("payment_source")] PayPalPaymentSource PaymentSource);

public sealed record PayPalCustomerRequest
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }
    [JsonPropertyName("merchant_customer_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MerchantCustomerId { get; init; }
}

public sealed record PayPalPaymentTokenResponse
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("customer")] public PayPalCustomerResponse Customer { get; init; } = new();
    [JsonPropertyName("payment_source")] public PayPalPaymentSourceResponse PaymentSource { get; init; } = new();
}

public sealed record PayPalCustomerResponse
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
}

public sealed record PayPalPaymentSourceResponse
{
    [JsonPropertyName("card")] public PayPalCardResponse? Card { get; init; }
}

public sealed record PayPalCardResponse
{
    [JsonPropertyName("brand")] public string Brand { get; init; } = string.Empty;
    [JsonPropertyName("last_digits")] public string LastDigits { get; init; } = string.Empty;
    [JsonPropertyName("expiry")] public string Expiry { get; init; } = string.Empty;
}

public sealed record PayPalLink
{
    [JsonPropertyName("href")] public string Href { get; init; } = string.Empty;
    [JsonPropertyName("rel")] public string Rel { get; init; } = string.Empty;
}

public sealed record PayPalTransactionSearchResponse
{
    [JsonPropertyName("transaction_details")] public List<PayPalTransactionDetail> TransactionDetails { get; init; } = new();
    [JsonPropertyName("page")] public int Page { get; init; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; init; }
}

public sealed record PayPalTransactionDetail
{
    [JsonPropertyName("transaction_info")] public PayPalTransactionInfo TransactionInfo { get; init; } = new();
}

public sealed record PayPalTransactionInfo
{
    [JsonPropertyName("transaction_id")] public string TransactionId { get; init; } = string.Empty;
    [JsonPropertyName("paypal_reference_id")] public string? PayPalReferenceId { get; init; }
    [JsonPropertyName("paypal_reference_id_type")] public string? PayPalReferenceIdType { get; init; }
    [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; init; }
    [JsonPropertyName("transaction_initiation_date")] public DateTimeOffset? TransactionInitiationDate { get; init; }
    [JsonPropertyName("transaction_updated_date")] public DateTimeOffset? TransactionUpdatedDate { get; init; }
    [JsonPropertyName("transaction_amount")] public PayPalMoney? TransactionAmount { get; init; }
    [JsonPropertyName("fee_amount")] public PayPalMoney? FeeAmount { get; init; }
    [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; init; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; init; }
    [JsonPropertyName("custom_field")] public string? CustomField { get; init; }
    [JsonPropertyName("instrument_type")] public string? InstrumentType { get; init; }
    [JsonPropertyName("instrument_sub_type")] public string? InstrumentSubType { get; init; }
}

public sealed record PayPalTokenResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; init; } = string.Empty;
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
}
