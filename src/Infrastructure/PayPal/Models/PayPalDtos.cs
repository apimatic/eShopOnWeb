using System;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Models;

internal sealed class PayPalMoneyDto
{
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    public static PayPalMoneyDto From(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    public decimal ToDecimal()
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            return 0m;
        }

        return decimal.Parse(Value, CultureInfo.InvariantCulture);
    }
}

internal sealed class PayPalLinkDto
{
    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Method { get; set; }
}

internal sealed class PayPalErrorDto
{
    public string? Name { get; set; }
    public string? Message { get; set; }

    [JsonPropertyName("debug_id")]
    public string? DebugId { get; set; }

    public PayPalErrorDetailDto[]? Details { get; set; }
}

internal sealed class PayPalErrorDetailDto
{
    public string? Field { get; set; }
    public string? Issue { get; set; }
    public string? Description { get; set; }
}

internal sealed class PayPalTokenResponseDto
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal sealed class PayPalCreateOrderRequestDto
{
    public string Intent { get; set; } = "AUTHORIZE";
    public PayPalPurchaseUnitRequestDto[] PurchaseUnits { get; set; } = Array.Empty<PayPalPurchaseUnitRequestDto>();
    public PayPalPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PayPalPurchaseUnitRequestDto
{
    [JsonPropertyName("reference_id")]
    public string? ReferenceId { get; set; }

    public string? Description { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    public PayPalAmountDto? Amount { get; set; }
    public PayPalItemDto[]? Items { get; set; }
    public PayPalShippingDto? Shipping { get; set; }
}

internal sealed class PayPalAmountDto
{
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    public string? Value { get; set; }
    public PayPalAmountBreakdownDto? Breakdown { get; set; }
}

internal sealed class PayPalAmountBreakdownDto
{
    [JsonPropertyName("item_total")]
    public PayPalMoneyDto? ItemTotal { get; set; }
}

internal sealed class PayPalItemDto
{
    public string? Name { get; set; }

    [JsonPropertyName("unit_amount")]
    public PayPalMoneyDto? UnitAmount { get; set; }

    public string? Quantity { get; set; }
    public string? Sku { get; set; }
    public string? Category { get; set; }
}

internal sealed class PayPalShippingDto
{
    public PayPalShippingNameDto? Name { get; set; }
    public PayPalAddressDto? Address { get; set; }
}

internal sealed class PayPalShippingNameDto
{
    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }
}

internal sealed class PayPalAddressDto
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

internal sealed class PayPalPaymentSourceDto
{
    public PayPalCardRequestDto? Card { get; set; }
}

internal sealed class PayPalCardRequestDto
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("billing_address")]
    public PayPalAddressDto? BillingAddress { get; set; }

    [JsonPropertyName("vault_id")]
    public string? VaultId { get; set; }

    [JsonPropertyName("stored_credential")]
    public PayPalStoredCredentialDto? StoredCredential { get; set; }
}

internal sealed class PayPalStoredCredentialDto
{
    [JsonPropertyName("payment_initiator")]
    public string? PaymentInitiator { get; set; }

    [JsonPropertyName("payment_type")]
    public string? PaymentType { get; set; }

    public string? Usage { get; set; }
}

internal sealed class PayPalOrderDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }

    [JsonPropertyName("purchase_units")]
    public PayPalPurchaseUnitDto[]? PurchaseUnits { get; set; }

    public PayPalLinkDto[]? Links { get; set; }
}

internal sealed class PayPalPurchaseUnitDto
{
    [JsonPropertyName("reference_id")]
    public string? ReferenceId { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    public PayPalPaymentCollectionDto? Payments { get; set; }
}

internal sealed class PayPalPaymentCollectionDto
{
    public PayPalAuthorizationDto[]? Authorizations { get; set; }
    public PayPalCaptureDto[]? Captures { get; set; }
    public PayPalRefundDto[]? Refunds { get; set; }
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

    [JsonPropertyName("update_time")]
    public string? UpdateTime { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }
}

internal sealed class PayPalCaptureDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }

    [JsonPropertyName("final_capture")]
    public bool? FinalCapture { get; set; }

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

internal sealed class PayPalCaptureRequestDto
{
    public PayPalMoneyDto? Amount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("final_capture")]
    public bool FinalCapture { get; set; } = true;
}

internal sealed class PayPalReauthorizeRequestDto
{
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalRefundRequestDto
{
    public PayPalMoneyDto? Amount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }
}

internal sealed class PayPalRefundDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalVaultPaymentTokenRequestDto
{
    public PayPalCustomerDto? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalVaultPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PayPalCustomerDto
{
    [JsonPropertyName("merchant_customer_id")]
    public string? MerchantCustomerId { get; set; }
}

internal sealed class PayPalVaultPaymentSourceDto
{
    public PayPalCardRequestDto? Card { get; set; }
}

internal sealed class PayPalVaultPaymentTokenDto
{
    public string? Id { get; set; }
    public PayPalCustomerDto? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalVaultedPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PayPalVaultedPaymentSourceDto
{
    public PayPalVaultedCardDto? Card { get; set; }
}

internal sealed class PayPalVaultedCardDto
{
    public string? Name { get; set; }

    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }

    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

internal sealed class PayPalTransactionSearchResponseDto
{
    [JsonPropertyName("transaction_details")]
    public PayPalTransactionDetailDto[]? TransactionDetails { get; set; }

    public int? Page { get; set; }

    [JsonPropertyName("total_items")]
    public int? TotalItems { get; set; }

    [JsonPropertyName("total_pages")]
    public int? TotalPages { get; set; }
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

    [JsonPropertyName("transaction_event_code")]
    public string? TransactionEventCode { get; set; }

    [JsonPropertyName("transaction_initiation_date")]
    public string? TransactionInitiationDate { get; set; }

    [JsonPropertyName("transaction_amount")]
    public PayPalMoneyDto? TransactionAmount { get; set; }

    [JsonPropertyName("fee_amount")]
    public PayPalMoneyDto? FeeAmount { get; set; }

    [JsonPropertyName("transaction_status")]
    public string? TransactionStatus { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_field")]
    public string? CustomField { get; set; }
}
