using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class PayPalAccessTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }
}

internal sealed class PayPalErrorResponse
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? DebugId { get; set; }
    public List<PayPalErrorDetail>? Details { get; set; }
}

internal sealed class PayPalErrorDetail
{
    public string? Issue { get; set; }
    public string? Description { get; set; }
    public string? Field { get; set; }
}

internal sealed class PayPalMoneyDto
{
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    public string? Value { get; set; }
}

internal sealed class PayPalLinkDto
{
    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Method { get; set; }
}

internal sealed class PayPalOrderRequest
{
    public string Intent { get; set; } = "AUTHORIZE";
    public List<PayPalPurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    public PayPalPaymentSourceRequest? PaymentSource { get; set; }
}

internal sealed class PayPalPurchaseUnitRequest
{
    [JsonPropertyName("reference_id")]
    public string? ReferenceId { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    public PayPalAmountRequest? Amount { get; set; }
    public List<PayPalItemRequest>? Items { get; set; }
}

internal sealed class PayPalAmountRequest
{
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    public string? Value { get; set; }
    public PayPalAmountBreakdown? Breakdown { get; set; }
}

internal sealed class PayPalAmountBreakdown
{
    [JsonPropertyName("item_total")]
    public PayPalMoneyDto? ItemTotal { get; set; }
}

internal sealed class PayPalItemRequest
{
    public string? Name { get; set; }
    public string? Sku { get; set; }
    public string? Description { get; set; }
    public string? Quantity { get; set; }
    public string? Category { get; set; } = "PHYSICAL_GOODS";

    [JsonPropertyName("unit_amount")]
    public PayPalMoneyDto? UnitAmount { get; set; }
}

internal sealed class PayPalPaymentSourceRequest
{
    public PayPalCardRequest? Card { get; set; }
}

internal sealed class PayPalCardRequest
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? Name { get; set; }

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("vault_id")]
    public string? VaultId { get; set; }

    [JsonPropertyName("billing_address")]
    public PayPalAddressRequest? BillingAddress { get; set; }
}

internal sealed class PayPalAddressRequest
{
    [JsonPropertyName("address_line_1")]
    public string? AddressLine1 { get; set; }

    [JsonPropertyName("address_line_2")]
    public string? AddressLine2 { get; set; }

    [JsonPropertyName("admin_area_1")]
    public string? AdminArea1 { get; set; }

    [JsonPropertyName("admin_area_2")]
    public string? AdminArea2 { get; set; }

    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }
}

internal sealed class PayPalOrderResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public string? Intent { get; set; }

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnitResponse>? PurchaseUnits { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceResponse? PaymentSource { get; set; }

    public List<PayPalLinkDto>? Links { get; set; }
}

internal sealed class PayPalPurchaseUnitResponse
{
    public PayPalPaymentsContainer? Payments { get; set; }
}

internal sealed class PayPalPaymentsContainer
{
    public List<PayPalAuthorizationDto>? Authorizations { get; set; }
    public List<PayPalCaptureDto>? Captures { get; set; }
}

internal sealed class PayPalAuthorizationDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }

    [JsonPropertyName("expiration_time")]
    public string? ExpirationTime { get; set; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; set; }
}

internal sealed class PayPalCaptureDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }

    [JsonPropertyName("seller_receivable_breakdown")]
    public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; set; }
}

internal sealed class PayPalSellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")]
    public PayPalMoneyDto? GrossAmount { get; set; }

    [JsonPropertyName("paypal_fee")]
    public PayPalMoneyDto? PaypalFee { get; set; }

    [JsonPropertyName("net_amount")]
    public PayPalMoneyDto? NetAmount { get; set; }
}

internal sealed class PayPalPaymentSourceResponse
{
    public PayPalCardResponse? Card { get; set; }
}

internal sealed class PayPalCardResponse
{
    public string? Name { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }

    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }
}

internal sealed class PayPalCaptureRequest
{
    public PayPalMoneyDto? Amount { get; set; }

    [JsonPropertyName("final_capture")]
    public bool FinalCapture { get; set; } = true;

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }
}

internal sealed class PayPalReauthorizeRequest
{
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalRefundRequest
{
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalRefundDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalSetupTokenRequest
{
    public PayPalCustomerDto? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceRequest? PaymentSource { get; set; }
}

internal sealed class PayPalCustomerDto
{
    public string? Id { get; set; }
}

internal sealed class PayPalSetupTokenResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalCustomerDto? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceResponse? PaymentSource { get; set; }

    public List<PayPalLinkDto>? Links { get; set; }
}

internal sealed class PayPalPaymentTokenRequest
{
    [JsonPropertyName("payment_source")]
    public PayPalTokenPaymentSource? PaymentSource { get; set; }
}

internal sealed class PayPalTokenPaymentSource
{
    public PayPalTokenReference? Token { get; set; }
}

internal sealed class PayPalTokenReference
{
    public string? Id { get; set; }
    public string? Type { get; set; }
}

internal sealed class PayPalPaymentTokenResponse
{
    public string? Id { get; set; }
    public PayPalCustomerDto? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceResponse? PaymentSource { get; set; }
}

internal sealed class PayPalTransactionSearchResponse
{
    [JsonPropertyName("transaction_details")]
    public List<PayPalTransactionDetail>? TransactionDetails { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }

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

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("transaction_event_code")]
    public string? TransactionEventCode { get; set; }

    [JsonPropertyName("transaction_status")]
    public string? TransactionStatus { get; set; }

    [JsonPropertyName("transaction_amount")]
    public PayPalMoneyDto? TransactionAmount { get; set; }

    [JsonPropertyName("fee_amount")]
    public PayPalMoneyDto? FeeAmount { get; set; }

    [JsonPropertyName("transaction_initiation_date")]
    public string? TransactionInitiationDate { get; set; }
}
