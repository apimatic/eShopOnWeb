using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

internal sealed class PayPalMoneyDto
{
    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
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
    public string CountryCode { get; set; } = "US";
}

internal sealed class PayPalStoredCredentialDto
{
    [JsonPropertyName("payment_initiator")]
    public string PaymentInitiator { get; set; } = "CUSTOMER";

    [JsonPropertyName("payment_type")]
    public string PaymentType { get; set; } = "ONE_TIME";

    [JsonPropertyName("usage")]
    public string Usage { get; set; } = "SUBSEQUENT";
}

internal sealed class PayPalCardDto
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
    public PayPalAddressDto? BillingAddress { get; set; }

    [JsonPropertyName("vault_id")]
    public string? VaultId { get; set; }

    [JsonPropertyName("last_digits")]
    public string? LastDigits { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("stored_credential")]
    public PayPalStoredCredentialDto? StoredCredential { get; set; }
}

internal sealed class PayPalPaymentSourceDto
{
    [JsonPropertyName("card")]
    public PayPalCardDto? Card { get; set; }
}

internal sealed class PayPalAmountBreakdownDto
{
    [JsonPropertyName("item_total")]
    public PayPalMoneyDto? ItemTotal { get; set; }
}

internal sealed class PayPalAmountDto
{
    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("breakdown")]
    public PayPalAmountBreakdownDto? Breakdown { get; set; }
}

internal sealed class PayPalItemDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public string Quantity { get; set; } = "1";

    [JsonPropertyName("unit_amount")]
    public PayPalMoneyDto UnitAmount { get; set; } = new();
}

internal sealed class PayPalPurchaseUnitRequestDto
{
    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("amount")]
    public PayPalAmountDto Amount { get; set; } = new();

    [JsonPropertyName("items")]
    public List<PayPalItemDto>? Items { get; set; }
}

internal sealed class PayPalCreateOrderRequestDto
{
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "AUTHORIZE";

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnitRequestDto> PurchaseUnits { get; set; } = new();

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PayPalLinkDto
{
    [JsonPropertyName("href")]
    public string? Href { get; set; }

    [JsonPropertyName("rel")]
    public string? Rel { get; set; }
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

internal sealed class PayPalPaymentCollectionDto
{
    [JsonPropertyName("authorizations")]
    public List<PayPalAuthorizationDto>? Authorizations { get; set; }

    [JsonPropertyName("captures")]
    public List<PayPalCaptureDto>? Captures { get; set; }
}

internal sealed class PayPalPurchaseUnitDto
{
    [JsonPropertyName("payments")]
    public PayPalPaymentCollectionDto? Payments { get; set; }
}

internal sealed class PayPalOrderDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnitDto>? PurchaseUnits { get; set; }

    [JsonPropertyName("links")]
    public List<PayPalLinkDto>? Links { get; set; }
}

internal sealed class PayPalCaptureRequestDto
{
    [JsonPropertyName("amount")]
    public PayPalMoneyDto? Amount { get; set; }

    [JsonPropertyName("final_capture")]
    public bool FinalCapture { get; set; } = true;
}

internal sealed class PayPalReauthorizeRequestDto
{
    [JsonPropertyName("amount")]
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalRefundRequestDto
{
    [JsonPropertyName("amount")]
    public PayPalMoneyDto? Amount { get; set; }
}

internal sealed class PayPalCustomerDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("merchant_customer_id")]
    public string? MerchantCustomerId { get; set; }
}

internal sealed class PayPalVaultRequestDto
{
    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceDto PaymentSource { get; set; } = new();

    [JsonPropertyName("customer")]
    public PayPalCustomerDto? Customer { get; set; }
}

internal sealed class PayPalPaymentTokenDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("customer")]
    public PayPalCustomerDto? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSourceDto? PaymentSource { get; set; }
}

internal sealed class PayPalOAuthTokenDto
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal sealed class PayPalErrorDetailDto
{
    [JsonPropertyName("issue")]
    public string? Issue { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
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

internal sealed class PayPalTransactionAmountDto
{
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

internal sealed class PayPalTransactionInfoDto
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
    public PayPalTransactionAmountDto? TransactionAmount { get; set; }

    [JsonPropertyName("fee_amount")]
    public PayPalTransactionAmountDto? FeeAmount { get; set; }

    [JsonPropertyName("custom_field")]
    public string? CustomField { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }
}

internal sealed class PayPalTransactionDetailDto
{
    [JsonPropertyName("transaction_info")]
    public PayPalTransactionInfoDto? TransactionInfo { get; set; }
}

internal sealed class PayPalSearchResponseDto
{
    [JsonPropertyName("transaction_details")]
    public List<PayPalTransactionDetailDto>? TransactionDetails { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }
}
