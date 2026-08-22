using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class PayPalMoneyDto
{
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

internal sealed class PayPalLinkDto
{
    [JsonPropertyName("href")]
    public string? Href { get; set; }

    [JsonPropertyName("rel")]
    public string? Rel { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }
}

internal sealed class PayPalErrorDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("debug_id")]
    public string? DebugId { get; set; }

    [JsonPropertyName("details")]
    public List<PayPalErrorDetailDto>? Details { get; set; }
}

internal sealed class PayPalErrorDetailDto
{
    [JsonPropertyName("issue")]
    public string? Issue { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("field")]
    public string? Field { get; set; }
}

internal sealed class PayPalTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }
}

internal sealed class PayPalOrderResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnitDto>? PurchaseUnits { get; set; }

    [JsonPropertyName("links")]
    public List<PayPalLinkDto>? Links { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PayPalPurchaseUnitDto
{
    [JsonPropertyName("payments")]
    public PayPalPaymentsDto? Payments { get; set; }
}

internal sealed class PayPalPaymentsDto
{
    [JsonPropertyName("authorizations")]
    public List<PayPalAuthorizationDto>? Authorizations { get; set; }

    [JsonPropertyName("captures")]
    public List<PayPalCaptureDto>? Captures { get; set; }
}

internal sealed class PayPalAuthorizationDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoneyDto? Amount { get; set; }

    [JsonPropertyName("expiration_time")]
    public string? ExpirationTime { get; set; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; set; }
}

internal sealed class PayPalCaptureDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoneyDto? Amount { get; set; }

    [JsonPropertyName("seller_receivable_breakdown")]
    public PayPalSellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; set; }
}

internal sealed class PayPalSellerReceivableBreakdownDto
{
    [JsonPropertyName("gross_amount")]
    public PayPalMoneyDto? GrossAmount { get; set; }

    [JsonPropertyName("paypal_fee")]
    public PayPalMoneyDto? PaypalFee { get; set; }

    [JsonPropertyName("net_amount")]
    public PayPalMoneyDto? NetAmount { get; set; }
}

internal sealed class PayPalRefundDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalPaymentSourceDto
{
    [JsonPropertyName("card")]
    public PayPalCardDto? Card { get; set; }
}

internal sealed class PayPalCardDto
{
    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

internal sealed class PayPalSetupTokenResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("customer")]
    public PayPalCustomerDto? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceDto? PaymentSource { get; set; }

    [JsonPropertyName("links")]
    public List<PayPalLinkDto>? Links { get; set; }
}

internal sealed class PayPalPaymentTokenResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("customer")]
    public PayPalCustomerDto? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PayPalCustomerDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

internal sealed class PayPalTransactionSearchResponse
{
    [JsonPropertyName("transaction_details")]
    public List<PayPalTransactionDetailDto>? TransactionDetails { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }
}

internal sealed class PayPalTransactionDetailDto
{
    [JsonPropertyName("transaction_info")]
    public PayPalTransactionInfoDto? TransactionInfo { get; set; }
}

internal sealed class PayPalTransactionInfoDto
{
    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("paypal_reference_id")]
    public string? PaypalReferenceId { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_field")]
    public string? CustomField { get; set; }

    [JsonPropertyName("transaction_event_code")]
    public string? TransactionEventCode { get; set; }

    [JsonPropertyName("transaction_status")]
    public string? TransactionStatus { get; set; }

    [JsonPropertyName("transaction_amount")]
    public PayPalMoneyDto? TransactionAmount { get; set; }

    [JsonPropertyName("transaction_initiation_date")]
    public string? TransactionInitiationDate { get; set; }

    [JsonPropertyName("transaction_updated_date")]
    public string? TransactionUpdatedDate { get; set; }
}
